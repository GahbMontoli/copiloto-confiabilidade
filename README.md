# Copiloto de Confiabilidade Operacional

![.NET](https://img.shields.io/badge/.NET-8.0-512BD4?logo=dotnet)
![Windows](https://img.shields.io/badge/Windows-WPF-0078D4?logo=windows)
![Status](https://img.shields.io/badge/status-prot%C3%B3tipo%20validado-2E8B57)

Protótipo local para Windows de um assistente conectado a um CMMS. O aplicativo mantém o companheiro 3D e substitui o antigo detector de logs por um fluxo de manutenção baseado em ativos, ordens de serviço, recorrência e casos históricos semelhantes.

## Executar

```powershell
Set-Location '.\copiloto-confiabilidade'
.\Executar-AssistentePatinho.ps1
```

Clique no patinho para abrir o painel. Selecione um ativo e uma ordem, use **Gerar triagem** para revisar os casos semelhantes ou **Simular nova falha** para criar uma ocorrência temporária. Nenhuma integração externa ou alteração de equipamento é executada.

## Escopo do protótipo

- cinco ativos simulados;
- doze ordens iniciais abertas e concluídas;
- conector CMMS local baseado em JSON;
- portão de qualidade executado antes da triagem, que bloqueia IDs duplicados, vínculos inválidos, campos mínimos ausentes, datas incoerentes e históricos concluídos sem causa ou resolução;
- histórico por ativo;
- identificação de recorrência;
- busca ponderada por código de falha, ativo, categoria, modelo e descrição;
- explicação dos critérios e pesos que compõem cada similaridade histórica;
- verificações recuperadas de ordens concluídas;
- evidências e limitações visíveis;
- simulação de nova ocorrência somente em memória.

Os dados ficam em `src\AssistentePatinho.App\sample-cmms-data.json`. A interface lê uma cópia desse arquivo no diretório da aplicação.

## Segurança e rastreabilidade

- O protótipo é somente leitura; a simulação não altera o arquivo JSON.
- Dados inconsistentes são rejeitados antes de alimentar a triagem.
- Casos semelhantes são apoio à decisão, não confirmação de causa raiz.
- Soluções históricas são exibidas com a ordem de origem.
- Intervenções continuam dependendo de validação técnica, segurança e aprovação humana.

## Analysis Core local

O primeiro núcleo de análise offline processa `.csv`, `.json`, `.log` e `.txt`
sem transmitir conteúdo ou chamar um modelo de linguagem. Ele aplica regras
estruturais versionadas, registra localização, severidade, evidência,
recomendação e limitação, e reutiliza resultados por SHA-256 em cache local.

```powershell
.\Analisar-Arquivo.ps1 -Path .\dados.csv -EvidenceClass synthetic
```

O relatório JSON usa classe `derived` para os achados e mantém separada a classe
de evidência declarada para a fonte. O cache padrão fica no perfil local do
Windows e não armazena o conteúdo original, apenas o relatório derivado. Arquivos
acima de 64 MiB e extensões não suportadas são recusados nesta versão.

As regras atuais detectam problemas estruturais de CSV, JSON inválido ou vazio,
proporção elevada de nulos e marcadores textuais de log. Marcadores como
`ERROR` e `WARN` são pistas lexicais: não confirmam falha, severidade ou causa
raiz. Excel, PDF, DXF/DWG, embeddings e modelos locais permanecem fora deste
primeiro marco.

## Validar

```powershell
.\Validar-AssistentePatinho.ps1
```

A validação compila o aplicativo e o analisador local em Release e executa vinte
e oito cenários do núcleo,
incluindo o contrato de exportação BI e a rejeição de backlog temporalmente inválido.

## Exportar dados para Power BI

```powershell
.\Exportar-PowerBI.ps1
```

O comando gera em `artifacts\power-bi`:

- `dim_asset.csv`;
- `fact_work_order.csv`;
- `bridge_work_order_check.csv`;
- `bridge_work_order_part.csv`;
- `manifest.json` com versão, execução, SHA-256 da fonte e contagens.

Todas as linhas são identificadas como `synthetic`. O pacote demonstra
modelagem, qualidade e integração; não representa dados reais de manutenção e
não cria dashboards HTML.

No contrato v2, `backlog_age_hours` registra horas corridas entre a abertura e o
instante UTC da exportação somente para ordens abertas. O campo fica vazio para
ordens concluídas e não representa horas trabalhadas, indisponibilidade nem MTTR.

## Gerar pacote para Windows

```powershell
.\Publicar-Windows.ps1
```

O script valida o projeto, publica a aplicação para `win-x64` e cria
`dist\Copiloto-Confiabilidade-win-x64.zip`. O pacote é dependente do .NET 8
Desktop Runtime e não altera configurações do Windows.

## Arquitetura

- `AssistentePatinho.Core`: contratos do CMMS, ativos, ordens e motor de triagem;
- `AssistentePatinho.App`: interface WPF, mascote 3D e dados demonstrativos;
- `AssistentePatinho.Export`: exportador tabular versionado para Power BI;
- `AssistentePatinho.Core.SmokeTests`: cenários executáveis sem dependência de
  serviços externos.

## Próximas integrações

O próximo conector deve implementar `ICmmsConnector` para consumir uma API real. Depois disso, entram autenticação, perfis de acesso, auditoria de consultas e criação de rascunhos de ordem mediante aprovação.

## Licença

MIT. Consulte `LICENSE`.
