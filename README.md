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
# Suíte completa: .NET 8 e CA SMTP temporária em container isolado
bash tests/run-tests.sh
dotnet run --project src/BSourceNotifier.API
```

Por padrão, a aplicação local usa a configuração de `appsettings.json` e a porta definida pelo perfil de execução. Com o perfil HTTP atual, a API fica em `http://localhost:5000`; em `Development`, a documentação fica em `/swagger`.

## Execução com Docker

```bash
cp docker/.env.example docker/.env.local
# Edite docker/.env.local com a configuração do seu ambiente.
docker compose --env-file docker/.env.local -f docker/docker-compose.yml up --build -d
docker compose --env-file docker/.env.local -f docker/docker-compose.yml down
```

O container expõe a porta `5000`. Use `--env-file docker/.env.local` também ao executar `docker-compose-local.yml`: o `env_file` do serviço injeta variáveis no container, mas não alimenta a interpolação `${...}` do Compose. Não versione arquivos com segredos. As variáveis do Compose sobrescrevem a configuração JSON usando `__` como separador.

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
| `Notification:Email:Smtp:AuthenticationMode` | `SMTP_AUTHENTICATION_MODE` | `Credentials` | `Credentials` autentica; `None` usa relay autorizado por IP, sem AUTH |
| `Notification:Email:Smtp:Host` | `SMTP_HOST` | — | Servidor SMTP |
| `Notification:Email:Smtp:Port` | `SMTP_PORT` | — | Porta SMTP |
| `Notification:Email:Smtp:Username` | `SMTP_USERNAME` | — | Obrigatório em `Credentials`; ignorado em `None` |
| `Notification:Email:Smtp:Password` | `SMTP_PASSWORD` | — | Obrigatória em `Credentials`; ignorada em `None` |
| `Notification:Email:Smtp:EnableSsl` | `SMTP_ENABLE_SSL` | `true` nos exemplos | STARTTLS; obrigatório em `None` |

#### SMTP autenticado (padrão)

```dotenv
EMAIL_ENABLED=true
EMAIL_FROM=noreply@exemplo.com
SMTP_AUTHENTICATION_MODE=Credentials
SMTP_HOST=smtp.exemplo.com
SMTP_PORT=587
SMTP_USERNAME=usuario-smtp
SMTP_PASSWORD=substitua-pelo-segredo
SMTP_ENABLE_SSL=true
```

Omitir `AuthenticationMode` mantém `Credentials`. Com e-mail habilitado, usuário e senha devem estar completos; campos vazios ou compostos apenas por espaços impedem a inicialização. `EnableSsl=false` continua permitido em `Credentials` por compatibilidade.

#### Relay autorizado por IP, sem autenticação SMTP

```dotenv
EMAIL_ENABLED=true
EMAIL_FROM=noreply@exemplo.com
SMTP_AUTHENTICATION_MODE=None
SMTP_HOST=relay.exemplo.com
SMTP_PORT=587
SMTP_USERNAME=
SMTP_PASSWORD=
SMTP_ENABLE_SSL=true
```

Nesse modo, o cliente usa `UseDefaultCredentials=false` e `Credentials=null`, ignora credenciais residuais e não envia `AUTH`, mesmo quando anunciado pelo relay. O administrador do relay deve autorizar o IP de saída efetivamente visto por ele (incluindo NAT) e permitir o remetente/destinatário desejados. A porta deve ser a porta STARTTLS informada pelo administrador.

Com e-mail habilitado, `None` com `EnableSsl=false` e modos desconhecidos impedem a inicialização. STARTTLS indisponível causa falha de envio, sem fallback para texto claro. O `SmtpClient` usa STARTTLS, não TLS implícito da porta 465. Certificados devem ter cadeia confiável, nome correspondente ao host e validade vigente. Para uma CA interna, instale sua raiz no trust store do container; não desabilite a validação do certificado.

As variáveis curtas acima são interpoladas pelo Compose. Em `docker run` ou configuração direta, use, por exemplo, `Notification__Email__Smtp__AuthenticationMode=None` e `Notification__Email__Smtp__EnableSsl=true`.

#### Diagnóstico e testes SMTP

Execute `bash tests/run-tests.sh` com Docker disponível. O script executa a suíte em .NET 8, cria certificados temporários e confia na CA somente no container descartável. Os testes verificam os comandos SMTP antes/depois de STARTTLS, ausência de AUTH, autenticação válida/recusada, certificados não confiáveis/com nome incorreto, configuração e envio HTTP até um servidor SMTP de teste. Isso não substitui o aceite de entrega no servidor de destino.

Falhas SMTP registram `NotificationId`, `SmtpHost`, `SmtpPort`, `AuthenticationMode`, `EnableSsl`, `SmtpStatusCode` e a exceção com suas causas internas. Ausência de STARTTLS aparece como falta de suporte a conexão segura; certificado inválido inclui a causa de autenticação TLS; credenciais recusadas aparecem como falha de autenticação SMTP. O `SmtpClient` pode apresentar o `530` recebido após uma tentativa de AUTH recusada, em vez do `535` original. Nenhuma senha, usuário SMTP ou corpo é acrescentado aos logs pelo canal.

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
| `channels` | array | Valores numéricos: `WebSocket=0`, `Email=1`, `Sms=2`, `Telegram=3`, `WhatsApp=4` |
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
    "channels": [1],
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

## Publicação e aceite no servidor de destino

O workflow manual `CI/CD — Notifier` executa a suíte antes de publicar no Docker Hub. A imagem usa `bsourcedev/bsource-notifier:X.Y.Z`, sem prefixo `hml`; versões existentes não são sobrescritas. O resumo e o artefato `notifier-release` registram versão, commit e digest verificado no registry. A tag Git `vX.Y.Z` registra a versão publicada e serve de base para o próximo cálculo SemVer.

No destino, use a referência imutável entregue:

```bash
docker pull bsourcedev/bsource-notifier@sha256:<digest-publicado>
```

Configure o container com o modo `None`, STARTTLS habilitado e os dados do relay. Preserve a chave/hash da API do ambiente. Faça o teste pela API do Notifier, usando `channels: [1]` (Email) e um identificador único no assunto, por exemplo `Aceite SMTP <versão> <data-hora>`.

O aceite requer:

1. Registrar versão, digest, horário, remetente, destinatário e identificador do teste.
2. Enviar uma notificação pelo endpoint documentado e verificar os logs do Notifier.
3. Confirmar a mensagem na caixa do destinatário, incluindo assunto e conteúdo; correlacionar com logs/ID de fila do relay quando disponíveis.
4. Registrar o resultado no ambiente de destino. Se houver apenas `202` ou aceitação pelo relay, a entrega continua pendente.

A operação do servidor de destino executa e confirma esse teste. Para rollback, reutilize o digest anterior e sua configuração compatível.

## Licença

Uso interno — BSource.
