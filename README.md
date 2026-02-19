# dotnet10-agent-framework-postgres-testcontainers-otel-grafana_consultaprodutos-v2
Exemplo em .NET 10 de Console Application que faz uso do projeto Microsoft Agent Framework, com integração com soluções de IA como Microsoft Foundry na consulta de informações de produtos em uma base PostgreSQL. Inclui o uso do Testcontainers para criação do ambiente de testes com os dados + observabilidade com Grafana + OpenTelemetry.

Aplicação em execução:

![Console App em execução](img/console-01.png)

Visualizando logs do Grafana Loki:

![Grafana Loki](img/loki-01.png)

Métricas enviadas para o Prometheus:

![Prometheus](img/prometheus-01.png)

Traces no Grafana Tempo:

![Prometheus](img/tempo-01.png)
