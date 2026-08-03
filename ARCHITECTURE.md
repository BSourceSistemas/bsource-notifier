# Arquitetura — BSourceNotifier

Este documento descreve a arquitetura implementada no repositório, o fluxo em runtime, os contratos entre camadas, os adaptadores de entrega e as implicações operacionais. Para executar e integrar o serviço, consulte o [README.md](README.md).

## Visão geral

O sistema é uma API ASP.NET Core 8 que recebe comandos síncronos por HTTP. O caso de uso cria uma entidade de notificação e percorre os canais registrados no container de injeção de dependências. Os adaptadores atuais são SMTP/RazorLight e SignalR.

```text
Sistema terceiro
     │  POST + X-Api-Key
     ▼
ASP.NET Core API
     │
     ▼
SendNotificationUseCase
     ├── EmailNotificationChannel ── RazorLight ── SMTP
     └── WebSocketNotificationChannel ── IHubContext ── SignalR
                                              ▲
                                      Clientes em tempo real
```

Não existe banco, broker, worker, armazenamento de notificações ou serviço externo de presença. O processamento ocorre dentro da requisição HTTP e a presença fica em um singleton local.

## Tecnologias

| Tecnologia | Versão/configuração | Responsabilidade |
|---|---|---|
| .NET / ASP.NET Core | 8.0 | Runtime, API REST, middleware e hosting |
| SignalR | incluído no ASP.NET Core | Conexões em tempo real e grupos |
| RazorLight | 2.3.1 | Compilação/renderização do corpo de e-mail |
| Serilog | 10.0.0 + Console 6.1.1 | Logging estruturado |
| Swashbuckle | 6.6.2 | Swagger em Development |
| Docker | SDK/ASP.NET 8.0 | Build e runtime do container |

## Camadas e dependências

```text
BSourceNotifier.API
 ├── Application
 ├── Infrastructure
 └── Contracts

BSourceNotifier.Application
 ├── Domain
 └── Contracts

BSourceNotifier.Infrastructure
 ├── Application   (implementa INotificationChannel)
 ├── Domain
 └── Contracts

BSourceNotifier.Contracts ── nenhuma dependência interna
BSourceNotifier.Domain    ── nenhuma dependência interna
```

### API (`src/BSourceNotifier.API`)

É o composition root. Configura Serilog, CORS, controllers, health checks, Swagger, SignalR, options e DI. Expõe `NotificationsController`, o filtro `ApiKeyAuthorizationFilter`, `/health` e `/hubs/notifications`.

### Application (`src/BSourceNotifier.Application`)

Contém `SendNotificationUseCase`, a porta `INotificationChannel` e `NotificationPresenceRegistry`. O caso de uso mapeia contratos para entidades, escolhe um adaptador por canal, captura falhas dos canais solicitados e decide o fallback offline.

### Contracts (`src/BSourceNotifier.Contracts`)

Define o contrato público de entrada: `SendNotificationCommand`, `NotificationTargetDto`, endpoints e o enum de canais. `target.data` permanece como objeto livre para preservar o contexto da aplicação consumidora.

### Domain (`src/BSourceNotifier.Domain`)

Define `Notification`, `NotificationTarget`, endpoints e enums de domínio. `NotificationDelivery` e `DeliveryStatus` modelam um possível status por canal, mas não são persistidos nem retornados pelo fluxo atual.

### Infrastructure (`src/BSourceNotifier.Infrastructure`)

Implementa os canais concretos, as options e o `NotificationHub`. Não contém fila nem persistência.

## Fluxo detalhado do envio

```text
1. Cliente chama POST /api/notifications/send.
2. ApiKeyAuthorizationFilter lê X-Api-Key e calcula SHA-256.
3. O hash é comparado em tempo constante com AuthorizedClients.
4. Controller delega para SendNotificationUseCase.
5. O caso de uso cria Notification com Guid e DateTime.UtcNow.
6. Para cada canal, encontra INotificationChannel por ChannelType.
7. O adaptador envia; falhas são logadas e não interrompem os canais seguintes.
8. Se fallback=true, ApplicationId estiver preenchido, o usuário estiver offline e Email não estiver na lista, tenta e-mail.
9. O controller responde 202 sem corpo.
```

O endpoint não valida regras de negócio como campos obrigatórios, correspondência entre canal e endpoint, e-mail válido ou lista de canais não vazia. A desserialização pode rejeitar JSON inválido, mas entradas semanticamente incompletas só serão percebidas pelo adaptador quando aplicável.

O fallback é avaliado com `NotificationPresenceRegistry.IsOnline(applicationId, userId)`. O envio de canais normais captura exceções individualmente; a tentativa do fallback ocorre fora desse bloco e uma exceção de fallback pode produzir erro HTTP não controlado.

## Autenticação e CORS

`ApiKeyAuthorizationFilter` protege apenas `POST /api/notifications/send`.

- Sem `X-Api-Key`: `401`.
- Com múltiplos valores, valor vazio ou hash sem correspondência: `403`.
- A chave apresentada nunca é armazenada; somente seu SHA-256 é comparado com `Notification:Security:AuthorizedClients`.
- `/health`, Swagger em Development e o hub não passam por esse filtro.

O CORS usa `Cors:AllowedOrigins` para origem exata e `Cors:AllowedOriginHosts` para permitir qualquer esquema/porta de um host. A política permite credenciais, headers e métodos. Ela é necessária principalmente para `negotiate` e conexões SignalR feitas por browser.

## SignalR e presença

`NotificationHub.OnConnectedAsync` obtém `userId` da identidade autenticada ou da query string e obtém `applicationId` da query string. Quando ambos existem, registra `connectionId` e adiciona a conexão ao grupo:

```text
app-{applicationId}-user-{userId}
```

No disconnect, o connection ID é removido. O canal WebSocket usa o grupo explícito de `target.endpoints.webSocket.group`; quando ausente, usa o mesmo grupo padrão. O evento é enviado pelo `IHubContext<NotificationHub>` com `{ id, title, message, createdAt, userId, data }`.

O registry é `ConcurrentDictionary` dentro de um singleton, portanto:

- presença não sobrevive a reinício;
- conexões e notificações em réplicas diferentes não compartilham presença;
- produção com escala horizontal exige afinidade de conexão e roteamento compatível ou um backplane/registro distribuído;
- o hub atual não expõe método para o cliente entrar em grupo explícito.

## Canal de e-mail

`EmailNotificationChannel`:

1. Retorna sem enviar se `Notification:Email:Enabled` for falso.
2. Exige `target.endpoints.email.to`.
3. Converte `JsonElement` para `ExpandoObject`/valores CLR.
4. Compila `notification.Message` como template RazorLight usando `target.data` como `@Model`.
5. Cria `MailMessage` HTML com `Title` como assunto.
6. Envia via `SmtpClient` usando host, porta, credenciais e SSL das options.

O serviço não faz fila, retry ou confirmação posterior do SMTP. Templates devem ser controlados pelo sistema confiável que chama a API; dados não confiáveis podem representar risco de injeção de conteúdo HTML/Razor.

## Canais planejados e extensibilidade

`Sms`, `Telegram` e `WhatsApp` estão presentes nos enums e nas options, mas não têm `INotificationChannel` registrado. Se solicitados, o caso de uso registra um warning de canal não registrado e continua.

Para adicionar um canal:

```csharp
public sealed class SmsNotificationChannel : INotificationChannel
{
    public NotificationChannelType ChannelType => NotificationChannelType.Sms;

    public Task SendAsync(Notification notification, CancellationToken cancellationToken)
    {
        // Integração com o provedor e tratamento de cancelamento.
        return Task.CompletedTask;
    }
}
```

Depois, registre `AddScoped<INotificationChannel, SmsNotificationChannel>()` no `Program.cs`, adicione options se necessário e cubra o adaptador com testes. O caso de uso o descobrirá automaticamente pelo `ChannelType`.

## Configuração e deployment

`NotificationOptions` contém toggles para WebSocket, Email, Sms, Telegram e WhatsApp. Variáveis de ambiente usam o caminho .NET com `__`, por exemplo `Notification__Email__Smtp__Password`.

O Dockerfile usa duas etapas: restauração/build com `mcr.microsoft.com/dotnet/sdk:8.0` e execução com `mcr.microsoft.com/dotnet/aspnet:8.0`. `ASPNETCORE_URLS=http://+:5000` e a porta 5000 são expostas. O Compose injeta CORS, API key, SMTP e toggles por variáveis.

Segredos SMTP e API keys devem vir de secret manager, variáveis protegidas ou arquivo fora do versionamento. A configuração versionada não deve conter credenciais reais; se uma credencial já tiver sido exposta, ela deve ser revogada/rotacionada.

## Logging e observabilidade

Serilog escreve no console com o template `[HH:mm:ss LVL] Mensagem {Properties}`. O nível padrão do sink é `Debug`, com overrides `Microsoft` e `System` em `Warning`. Há logs de autenticação, seleção/grupo do WebSocket e falhas por canal.

Não há métricas, tracing, endpoint de status de entrega ou correlação exposta no contrato HTTP. Em operação, recomenda-se adicionar correlação por proxy/cliente e monitorar logs de falha, latência e taxa de `5xx`.

## Testes

O projeto de testes contém testes de integração da autenticação da API, cobertura de `401`, `403`, `202`, saúde/Swagger em Development e não exposição de chave/hash/dados sensíveis nos logs. Execute:

```bash
dotnet test BSourceNotifier.sln
```

## Estrutura do projeto

```text
bsource-notifier/
├── README.md
├── ARCHITECTURE.md
├── BSourceNotifier.sln
├── docker/
│   ├── Dockerfile
│   ├── docker-compose.yml
│   └── docker-compose-local.yml
├── src/
│   ├── BSourceNotifier.API/
│   │   ├── Controllers/NotificationsController.cs
│   │   ├── Security/                  # API key e filtro
│   │   ├── Program.cs                 # composition root
│   │   ├── appsettings*.json
│   │   └── serilog.json
│   ├── BSourceNotifier.Application/
│   │   ├── Interfaces/INotificationChannel.cs
│   │   ├── UseCases/SendNotificationUseCase.cs
│   │   └── NotificationPresenceRegistry.cs
│   ├── BSourceNotifier.Contracts/     # DTOs, command e enum público
│   ├── BSourceNotifier.Domain/        # entidades e enums internos
│   └── BSourceNotifier.Infrastructure/
│       ├── Channels/                  # SMTP e SignalR
│       ├── Options/NotificationOptions.cs
│       └── SignalR/NotificationHub.cs
└── tests/BSourceNotifier.API.Tests/
```
