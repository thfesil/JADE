# MEMORIA COLETIVA: JADE (JET ACTIVE DIRECTORY EMULATOR)
> Repositorio Central de Conhecimento Arquitetural e Tecnico Compartilhado entre Modelos de Inteligencia Artificial e Engenheiros.
> Familia do Sistema: Jade 1. Revisao documental inicial: 07/11/2026.

---

## 1. DIRETIVAS MANDATORIAS PARA AGENTES DE INTELIGENCIA ARTIFICIAL

> [!IMPORTANT]
> REGRAS INEGOCIAVEIS DE GOVERNANCA:
> 1. CONSULTA PREVIA OBRIGATORIA: Antes de modificar codigo, propor refatoracoes ou responder sobre a arquitetura do projeto, consulte este documento. Ele contem o estado atual, convencoes operacionais e licoes aprendidas de iteracoes anteriores.
> 2. VERSIONAMENTO FIXO POR REVISAO (REGRA 1):
>    - O versionamento da familia nao se altera sem determinacao explicita de migracao de plataforma (ex.: transicao futura para Jade 2).
>    - E estritamente vedada a adocao de esquemas semanticos arbitrarios (como 1.2.3, 1.9.4).
>    - As iteracoes dentro da familia 1 seguem estritamente o padrao de revisao:
>      - Jade 1 (Rev 1)
>      - Jade 1 (Rev 2)
>      - Jade 1 (Rev 3)
>      - Sucessivas revisoes incrementais.
> 3. ISOLAMENTO DE FAMILIAS EM BRANCHES DO GIT (REGRA 2):
>    - E proibido manter pastas de familias legadas na raiz do branch ativo.
>    - Cada familia possui seu branch dedicado no repositorio:
>      - Branch main: Reservado exclusivamente para a versao ativa em producao.
>      - Branches de preservacao historica: v1, etc.
> 4. SINCRONIZACAO IMEDIATA DE CORRECOES (REGRA 3):
>    - Toda e qualquer correcao de bug, excecao de execucao, falha de integracao ou erro de sistema deve ser commitada e enviada imediatamente (git push) para o repositorio remoto.
>    - Nao e permitido reter correcoes localmente. Uma vez validada a solucao por testes, a sincronizacao deve ser realizada de pronto.
> 5. RIGOR DE PROFISSIONALISMO PARA INTELIGENCIAS ARTIFICIAIS (REGRA 4):
>    - E terminantemente proibida a utilizacao de travessoes (em-dash ou en-dash), emojis, girias, expressoes informais ou qualquer recurso visual ou estilistico que degrade o profissionalismo corporativo.
>    - Esta proibicao aplica-se de forma ampla e irrestrita a documentacoes, codigos-fonte, comentarios, interfaces graficas, logs de execucao e mensagens de controle de versao (commits).
> 6. ATUALIZACAO DO HISTORICO DE ENGENHARIA: Qualquer alteracao de regra de negocio, resolucao de falha critica, revisao de contrato de API ou nova funcionalidade deve ser documentada na secao Registro de Execucao e Historico de Engenharia ao final deste documento.
> 7. PRESERVACAO HISTORICA: E proibido remover ou sobrescrever registros cronologicos de outros modelos ou desenvolvedores.
> 8. CONFORMIDADE DE SEGURANCA E PRIVACIDADE: E terminantemente proibido registrar senhas em texto claro, credenciais de servico, chaves privadas, certificados confidenciais ou dados pessoais reais neste arquivo ou em qualquer artefato sob controle de versao.
> 9. ATUALIZACAO DO README.MD (REGRA 5): A cada push ou conjunto de novas funcionalidades subidas para o repositorio remoto, e obrigatorio atualizar a secao 'Novidades Recentes' do README.md principal para manter a documentacao publica alinhada com as capacidades e arquitetura mais recentes da plataforma.
> 10. CHECKLIST DE CONFORMIDADE MANDATORIO (REGRA 6): Ao finalizar qualquer atividade, intervencao ou demanda, o agente deve obrigatoriamente verificar se todas as regras e diretivas anteriores foram integralmente cumpridas antes da entrega.

---

## 2. VISAO GERAL DA ARQUITETURA DO JADE

Jade e um emulador autônomo e de alta fidelidade do motor de banco de dados do Microsoft Active Directory (NTDS DSA), projetado para criar e operar bases de dados nativas Jet Blue ESE (Extensible Storage Engine) atraves da biblioteca Win32 `esent.dll` presente em todos os sistemas operacionais Windows.

```mermaid
flowchart TD
    subgraph Consumidores["Consumidores (ex: Atom, Test Suites, CI/CD)"]
        ConsumerApp["Aplicacoes Corporativas / Testes"] -->|C# Managed API| JadeCore["Jade.Core (Gerenciador de Schema e Objetos AD)"]
    end

    subgraph NativeLayer["Camada Nativa e Interop"]
        JadeCore --> JadeNative["Jade.Native (P/Invoke Win32 esent.dll)"]
        JadeNative --> EsentDll["C:\\Windows\\System32\\esent.dll (Jet Blue)"]
    end

    subgraph Storage["Persistencia em Disco"]
        EsentDll --> DitFile["Arquivo de Banco de Dados (.dit)"]
        EsentDll --> WalLogs["Logs Transacionais WAL (.log)"]
    end
```

### Componentes da Solucao:

1. **Jade.Native (`src/Jade.Native/`):**
   - Declaracoes P/Invoke de alta performance e tipos de interoperabilidade com a API Win32 do ESE (`JetInit`, `JetBeginSession`, `JetCreateDatabase`, `JetCreateTable`, `JetAddColumn`, `JetSetColumn`, `JetSeek`, `JetCommitTransaction`).
2. **Jade.Core (`src/Jade.Core/`):**
   - Motor de emulacao do schema do Active Directory:
     - Tabela `datatable`: Mapeamento hierarquico DNT (Distinguished Name Tag) e PDNT (Parent DNT), simulando classes de objeto (user, organizationalUnit, container) e atributos chave (`sAMAccountName`, `userPrincipalName`, `userAccountControl`, `unicodePwd`, `objectGUID`, `objectSid`).
     - Tabela `fsmo_table`: Controle do RID Pool Master e simulacao injetavel de esgotamento de RIDs (erro Win32 8209 / WILL_NOT_PERFORM).
     - Contêiner `CN=Deleted Objects`: Suporte ao ciclo de vida e controle LDAP Show Deleted (`1.2.840.113556.1.4.417`).
3. **Jade.Tests (`tests/Jade.Tests/`):**
   - Bateria de testes de integridade transacional, persistencia, velocidade de busca e cenarios de falha FSMO.

---

## 3. REGISTRO DE EXECUCAO E HISTORICO DE ENGENHARIA

### [07/11/2026] - Inicializacao do Repositorio Jade 1 (Rev 1)
- **Modelo:** Gemini 3.8 Flash
- **Contexto:** Criacao do repositorio independente para o motor Jade (Jet Active Directory Emulator).
- **Acoes Executadas:**
  1. Criacao da solucao Jade.sln e projetos Jade.Core, Jade.Native e Jade.Tests.
  2. Implementacao formal das diretrizes mandatorias de governanca em MEMORIA_COLETIVA.md.
  3. Validacao da compilacao com zero erros e inicializacao do repositorio Git local.
- **Recomendacoes Tecnicas:**
  - Implementar as declaracoes P/Invoke de Jet Blue em Jade.Native e criar o primeiro teste de criacao de banco de dados .dit em Jade.Tests.
