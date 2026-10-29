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
- histórico por ativo;
- identificação de recorrência;
- busca ponderada por código de falha, ativo, categoria, modelo e descrição;
- verificações recuperadas de ordens concluídas;
- evidências e limitações visíveis;
- simulação de nova ocorrência somente em memória.

Os dados ficam em `src\AssistentePatinho.App\sample-cmms-data.json`. A interface lê uma cópia desse arquivo no diretório da aplicação.

## Segurança e rastreabilidade

- O protótipo é somente leitura; a simulação não altera o arquivo JSON.
- Casos semelhantes são apoio à decisão, não confirmação de causa raiz.
- Soluções históricas são exibidas com a ordem de origem.
- Intervenções continuam dependendo de validação técnica, segurança e aprovação humana.

## Validar

```powershell
.\Validar-AssistentePatinho.ps1
```

A validação compila o aplicativo em Release e executa sete cenários de fumaça
do núcleo de triagem.

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
- `AssistentePatinho.Core.SmokeTests`: cenários executáveis sem dependência de
  serviços externos.

## Próximas integrações

O próximo conector deve implementar `ICmmsConnector` para consumir uma API real. Depois disso, entram autenticação, perfis de acesso, auditoria de consultas e criação de rascunhos de ordem mediante aprovação.

## Licença

MIT. Consulte `LICENSE`.
