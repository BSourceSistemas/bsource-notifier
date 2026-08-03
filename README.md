# BSourceNotifier

Serviço HTTP de notificações multicanal. Um sistema de origem envia um comando para a API e o BSourceNotifier encaminha a notificação para e-mail e/ou para clientes conectados ao SignalR.

Este README documenta o comportamento implementado atualmente, como executar o serviço e tudo o que um sistema terceiro precisa para integrá-lo. A visão técnica e a organização interna estão em [ARCHITECTURE.md](ARCHITECTURE.md).

## Funcionalidades e limites atuais

- E-mail via SMTP, com corpo HTML renderizado por RazorLight.
- Entrega em tempo real por SignalR, usando o evento `notification`.
- Processamento sequencial e independente dos canais solicitados: uma falha é registrada e não impede a tentativa dos canais seguintes.
- Fallback opcional de e-mail quando o usuário está offline no SignalR.
- Autenticação da API de envio por chave no header `X-Api-Key`; o servidor armazena apenas o SHA-256 da chave.
- Health check em `/health` e Swagger somente no ambiente `Development`.

Não há banco de dados, fila, histórico de notificações, retry automático, idempotência ou confirmação de leitura/entrega. O status `202 Accepted` significa que a requisição foi aceita pelo endpoint; não significa que todos os canais entregaram a mensagem. SMS, Telegram e WhatsApp existem no contrato, mas ainda não possuem implementações registradas.

## Canais

| Canal | Situação | Endpoint necessário |
|---|---|---|
| `Email` | Implementado | `target.endpoints.email.to` |
| `WebSocket` | Implementado via SignalR | `target.endpoints.webSocket.group` é opcional |
| `Sms` | Planejado | Não implementado |
| `Telegram` | Planejado | Não implementado |
| `WhatsApp` | Planejado | Não implementado |

Um canal implementado pode ser desabilitado na configuração. Nesse caso ele é ignorado sem erro e a API ainda pode responder `202`.

## Requisitos para integrar um sistema terceiro

O consumidor precisa:

1. Conhecer a URL base do ambiente, por exemplo `https://notifier.exemplo.com`.
2. Receber uma chave de API por canal seguro. Envie a chave exatamente como recebida no header `X-Api-Key`; não use `Bearer`, não altere maiúsculas/minúsculas e não inclua espaços.
3. Enviar `POST /api/notifications/send` com `Content-Type: application/json`.
4. Usar um `applicationId` estável para identificar a aplicação consumidora e um `target.userId` estável para identificar o destinatário. Esses valores também são usados para presença e agrupamento do SignalR.
5. Informar os endpoints correspondentes aos canais escolhidos. O serviço não valida antecipadamente a coerência entre `channels` e `endpoints`; o integrador deve fazê-lo.
6. Para receber WebSocket, conectar-se ao hub com `applicationId` e `userId` na query string e permitir a origem no CORS do servidor.
7. Tratar `202`, `401`, `403` e erros HTTP como resultados distintos e implementar sua própria observabilidade/retry, caso necessário.

A chave real fica somente no sistema terceiro ou em seu cofre de segredos. No BSourceNotifier deve ser configurado o hash SHA-256 em hexadecimal. Nunca coloque a chave real em código, Git, logs ou payloads.

## Execução local

Pré-requisito: .NET SDK 8.

```bash
dotnet restore BSourceNotifier.sln
dotnet build BSourceNotifier.sln
dotnet test BSourceNotifier.sln
dotnet run --project src/BSourceNotifier.API
```

Por padrão, a aplicação local usa a configuração de `appsettings.json` e a porta definida pelo perfil de execução. Com o perfil HTTP atual, a API fica em `http://localhost:5000`; em `Development`, a documentação fica em `/swagger`.

## Execução com Docker

```bash
docker compose -f docker/docker-compose.yml up --build -d
docker compose -f docker/docker-compose.yml down
```

O container expõe a porta `5000`. O arquivo `docker-compose-local.yml` carrega adicionalmente `docker/.env.local`; não versione esse arquivo se ele contiver segredos. As variáveis do Compose sobrescrevem a configuração JSON usando `__` como separador.

## Configuração do servidor

### Segurança da API

O endpoint de envio exige `X-Api-Key`. Chaves ausentes retornam `401 Unauthorized`; chaves inválidas, duplicadas ou com formatação diferente da original retornam `403 Forbidden`.

Para gerar uma chave e seu hash:

```bash
API_KEY="$(openssl rand -base64 32)"
printf '%s' "$API_KEY" | sha256sum
```

Configure o hash, não `API_KEY`, no servidor:

| Configuração | Variável Docker/ambiente | Descrição |
|---|---|---|
| `Notification:Security:AuthorizedClients:0:Name` | `NOTIFICATION_SECURITY_CLIENT_0_NAME` | Nome do cliente nos logs |
| `Notification:Security:AuthorizedClients:0:ApiKeyHash` | `NOTIFICATION_SECURITY_CLIENT_0_API_KEY_HASH` | Hash SHA-256 hexadecimal da chave |

Para vários clientes, use os índices `1`, `2` etc. O filtro não registra a chave nem seu hash nos logs.

### CORS e SignalR

| Configuração | Variável | Descrição |
|---|---|---|
| `Cors:AllowedOrigins` | `CORS_ALLOWED_ORIGINS` | Origens exatas, separadas por vírgula, incluindo esquema e porta |
| `Cors:AllowedOriginHosts` | `CORS_ALLOWED_ORIGIN_HOSTS` | Hosts/IPs permitidos, independentemente da porta |

O CORS permite headers, métodos e credenciais. Para um front-end em outro domínio, configure a origem exata; para desenvolvimento, pode ser suficiente autorizar o host. O hub não aplica o filtro `X-Api-Key` do endpoint de envio; a segurança do acesso ao hub deve ser tratada pela rede, autenticação do host/proxy ou evolução do próprio hub.

### E-mail/SMTP

| Configuração | Variável | Padrão | Descrição |
|---|---|---:|---|
| `Notification:Email:Enabled` | `EMAIL_ENABLED` | `true` | Ativa o canal |
| `Notification:Email:From` | `EMAIL_FROM` | — | Remetente |
| `Notification:Email:Smtp:Host` | `SMTP_HOST` | — | Servidor SMTP |
| `Notification:Email:Smtp:Port` | `SMTP_PORT` | — | Porta SMTP |
| `Notification:Email:Smtp:Username` | `SMTP_USERNAME` | — | Usuário SMTP |
| `Notification:Email:Smtp:Password` | `SMTP_PASSWORD` | — | Senha/app password |
| `Notification:Email:Smtp:EnableSsl` | `SMTP_ENABLE_SSL` | — | TLS/SSL |

### Toggles dos canais

`WEBSOCKET_ENABLED`, `SMS_ENABLED`, `TELEGRAM_ENABLED` e `WHATSAPP_ENABLED` correspondem, respectivamente, a `Notification:WebSocket:Enabled`, `Notification:Sms:Enabled`, `Notification:Telegram:Enabled` e `Notification:WhatsApp:Enabled`. Somente WebSocket e Email têm adaptadores no código atual.

## API de envio

### `POST /api/notifications/send`

Headers obrigatórios:

```http
Content-Type: application/json
X-Api-Key: <chave-real>
```

Corpo:

| Campo | Tipo | Descrição |
|---|---|---|
| `applicationId` | string | Identificador da aplicação de origem; necessário para o agrupamento padrão e para o fallback offline |
| `title` | string | Assunto do e-mail e título do evento SignalR |
| `message` | string | HTML/Razor para e-mail; texto/HTML literal no evento SignalR |
| `channels` | array | Valores `Email`, `WebSocket`, `Sms`, `Telegram`, `WhatsApp` |
| `target.userId` | string | Identificador do destinatário |
| `target.endpoints.email.to` | string | E-mail, quando `Email` for solicitado |
| `target.endpoints.webSocket.group` | string | Grupo SignalR opcional |
| `target.data` | object | Dados livres; no e-mail são o model Razor e no SignalR são enviados integralmente |
| `emailFallbackWhenOffline` | boolean | Se `true`, envia e-mail quando não houver conexão SignalR para o par aplicação/usuário; exige endpoint de e-mail |

Exemplo mínimo de e-mail:

```bash
curl -X POST http://localhost:5000/api/notifications/send \
  -H 'Content-Type: application/json' \
  -H 'X-Api-Key: chave-configurada-no-servidor' \
  -d '{
    "applicationId": "billing",
    "title": "Pedido aprovado",
    "message": "<h1>Olá @Model.Name</h1><p>Pedido @Model.OrderId aprovado.</p>",
    "channels": ["Email"],
    "target": {
      "userId": "user-123",
      "endpoints": { "email": { "to": "cliente@exemplo.com" } },
      "data": { "Name": "João", "OrderId": "A-1020" }
    }
  }'
```

O Razor usa os nomes das propriedades de `target.data`; por exemplo, `@Model.Name` corresponde a `Name`. O conteúdo é enviado como HTML. Valide e higienize templates e dados antes de permitir que sejam fornecidos por usuários finais.

Resposta normal: `202 Accepted`, sem corpo. A requisição percorre os canais durante o processamento do endpoint; não existe consulta posterior de status.

| Status | Significado |
|---:|---|
| `202` | Comando aceito; não é recibo de entrega |
| `401` | `X-Api-Key` ausente |
| `403` | Chave inválida, duplicada ou com prefixo/espaços |
| `5xx` | Falha não tratada da aplicação/infraestrutura |

## SignalR

Hub: `/hubs/notifications`. O cliente deve conectar com ambos os parâmetros:

```javascript
const connection = new signalR.HubConnectionBuilder()
  .withUrl("https://notifier.exemplo.com/hubs/notifications?applicationId=billing&userId=user-123")
  .withAutomaticReconnect()
  .build();

connection.on("notification", payload => {
  console.log(payload.title, payload.message, payload.data);
});

await connection.start();
```

Sem `group`, o servidor envia para `app-{applicationId}-user-{userId}`. O hub adiciona automaticamente a conexão a esse grupo quando `applicationId` e `userId` estão presentes. Se for informado um `group` explícito no payload, o cliente deve entrar nesse grupo por um mecanismo adicional; o hub atual não expõe método público para ingressar em grupos arbitrários.

Evento `notification`:

```json
{
  "id": "guid",
  "title": "Pedido aprovado",
  "message": "...",
  "createdAt": "2026-08-03T15:00:00Z",
  "userId": "user-123",
  "data": { "orderId": "A-1020" }
}
```

O registro de presença é somente em memória. Em múltiplas réplicas, o cliente e o envio precisam alcançar a mesma instância, ou a aplicação deve ganhar um backplane/registro distribuído.

## Health check e documentação

- `GET /health` é público e retorna `200` quando a aplicação está disponível. Atualmente não verifica SMTP, SignalR ou dependências externas.
- `GET /swagger` e `/swagger/index.html` só são publicados quando `ASPNETCORE_ENVIRONMENT=Development`.

## Observabilidade e operação

O Serilog escreve logs estruturados no console. Os logs registram autenticação, grupo SignalR, canal e falhas, mas não devem receber chaves ou dados sensíveis. Como não há persistência nem fila, o operador deve acompanhar falhas em tempo real; reinícios perdem a presença em memória e não reprocessam mensagens.

## Licença

Uso interno — BSource.
