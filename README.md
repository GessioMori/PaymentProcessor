# PaymentProcessor

PaymentProcessor é uma solução baseada em .NET 9 que processa pagamentos de forma assíncrona, utilizando Redis como fila e Nginx como proxy reverso. A aplicação está dividida em três componentes principais:

- **API** (`PaymentProcessor.Api`): recebe requisições de pagamento e envia para a fila Redis.
- **Worker** (`PaymentProcessor.Worker`): consome a fila Redis, envia pagamentos para o processador externo via HTTP e mantém estatísticas no Redis.
- **Shared Library** (`PaymentProcessor.Shared`): contém entidades, DTOs e classes compartilhadas entre API e Worker.

## Estrutura do Projeto

```

PaymentProcessor/
├─ docker-compose/
│  ├─ docker-compose.yml
│  └─ nginx.conf
├─ PaymentProcessor.Api/
│  ├─ Dockerfile
│  └─ Program.cs
├─ PaymentProcessor.Worker/
│  ├─ Dockerfile
│  └─ PaymentProcessorWorker.cs
├─ PaymentProcessor.Shared/
│  ├─ Payment.cs
│  ├─ PaymentRecord.cs
│  ├─ RequestStats.cs
│  ├─ StatsResponse.cs
│  └─ JsonContext.cs

````

## Pré-requisitos

- Docker >= 24.0
- Docker Compose >= 2.17
- .NET 9 SDK (para build local opcional)

## Configuração

### Docker Compose

O `docker-compose.yml` define os serviços:

- **api1/api2**: instâncias da API com Unix domain sockets em `/sockets`.
- **worker**: processa os pagamentos consumindo a fila Redis.
- **nginx**: proxy reverso para balanceamento entre `api1` e `api2`.
- **redis**: fila e armazenamento de métricas.

### Volumes

```yaml
volumes:
  sockets: {}
````

O volume `/sockets` é compartilhado entre Nginx e as APIs para comunicação via Unix socket.

### Variáveis de Ambiente

* `ASPNETCORE_ENVIRONMENT=Production` nas APIs
* `SOCKET_PATH=/sockets/api1.sock` ou `/sockets/api2.sock`
* `DOTNET_ENVIRONMENT=Production` no Worker

---

## Build e Execução

No diretório raiz do repositório:

```bash
docker-compose build
docker-compose up -d
```

Verifique logs:

```bash
docker-compose logs -f api1
docker-compose logs -f worker
```

---

## Endpoints

### Processar Pagamento

```http
POST /payments
Content-Type: application/json

{
  "correlationId": "guid",
  "amount": 100.0
}
```

* A API envia o pagamento para a fila Redis (fire-and-forget).
* O Worker consome a fila e envia para o processador externo via HTTP.

### Consultar Estatísticas

```http
GET /payments-summary?from={dataInicial}&to={dataFinal}
```

Retorna JSON com estatísticas de pagamentos processados (default e fallback).

---

## Observações

* O Worker utiliza `HttpClient` com conexões HTTP/2 e limites de concorrência para alto throughput.
* O Nginx está configurado com `least_conn` e Unix domain sockets para reduzir latência.
* Falhas no processamento são automaticamente re-enfileiradas para retry.
* Redis armazena a lista de pagamentos e as métricas de estatísticas.

---