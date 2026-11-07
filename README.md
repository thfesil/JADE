# Jade: Jet Active Directory Emulator

Jade e um emulador autonomo, leve e de alta fidelidade do motor de banco de dados do Microsoft Active Directory (NTDS.DIT), implementado diretamente sobre o motor nativo Jet Blue ESE (Extensible Storage Engine / esent.dll) do Windows.

---

## Novidades Recentes (Jade 1 - Rev 3)

* Conexao integral da camada gerenciada .NET 8 com o motor nativo em C atraves de P/Invoke de alta performance e DllImportResolver dinamico baseado em AppContext.BaseDirectory.
* Implementacao da camada Jade.Native com mapeamento tipado de structs e metodos de interoperabilidade Win32 Cdecl.
* Implementacao da camada de dominio Jade.Core com a classe de controle JadeDatabase, modelo de dados JadeDirectoryEntry e gestao automatica de ciclo de vida IDisposable.
* Bateria de testes unitarios automatizados em xUnit no projeto Jade.Tests com 100% de aprovacao cobrindo criacao de banco, busca indexada B+Tree por sAMAccountName, controle de flags UAC 512/514, hashing de senhas, simulacao de esgotamento de RIDs (Win32 8209) e ciclo de vida de exclusao Tombstone.

---

## Estrutura da Solucao

* **native/**: Motor central de alto desempenho desenvolvido em C ANSI/C99.
  * **include/jade.h**: Contratos publicos da API C, estruturas e definicoes de schema.
  * **src/jade_core.c**: Implementacao direta sobre a biblioteca esent.dll do Windows.
  * **src/main.c**: Executavel de linha de comando e suite de auto-teste.
  * **Makefile**: Automacao de compilacao compativel com MinGW-w64 (GCC).
* **src/Jade.Native**: Interoperabilidade C# gerenciada via P/Invoke com a biblioteca jade.dll e esent.dll.
* **src/Jade.Core**: Camada de dominio e servicos de alto nivel para emulacao do Active Directory (JadeDatabase, JadeDirectoryEntry, JadeException).
* **tests/Jade.Tests**: Suite automatizada de testes xUnit cobrindo persistencia, transacoes e cenarios de falha FSMO.

---

## Governanca e Memoria Tecnica

Consulte o arquivo [MEMORIA_COLETIVA.md](./MEMORIA_COLETIVA.md) para diretivas mandatorias e historico de revisoes.
