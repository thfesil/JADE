# Jade: Jet Active Directory Emulator

Jade e um emulador autonomo, leve e de alta fidelidade do motor de banco de dados do Microsoft Active Directory (NTDS.DIT), implementado diretamente sobre o motor nativo Jet Blue ESE (Extensible Storage Engine / esent.dll) do Windows.

---

## Novidades Recentes (Jade 1 - Rev 2)

* Implementacao completa do motor nativo em C (jade.dll e jade.exe) com persistencia real em formato ESE B+Tree.
* Modelagem e criacao da tabela datatable espelhando a estrutura interna do NTDS.DIT com DNT (chave primaria autoincremental), PDNT (Parent DNT) e indices B+Tree dedicados (DNT_Index, PDNT_Index, SAM_Index, GUID_Index).
* Suporte nativo aos atributos de seguranca e identidade: sAMAccountName, userPrincipalName, unicodePwd, userAccountControl (UAC 512 ativo / 514 desativado), objectGUID com geracao automatica de 128 bits e objectSid.
* Criacao da tabela fsmo_table para gestao do RID Pool Master e alocacao sequencial de RIDs, incluindo modulo de injecao de falhas com emulacao do erro Win32 8209 (ERROR_DS_RIDM_UNWILLING_TO_PERFORM / 00002011).
* Implementacao do ciclo de vida de exclusao logica (Tombstone) atraves da marcacao isDeleted e renomeacao automatica de sAMAccountName.
* Suite de auto-validacao em 8 etapas com 100% de sucesso e finalizacao transacional limpa.

---

## Estrutura da Solucao

* **native/**: Motor central de alto desempenho desenvolvido em C ANSI/C99.
  * **include/jade.h**: Contratos publicos da API C, estruturas e definicoes de schema.
  * **src/jade_core.c**: Implementacao direta sobre a biblioteca esent.dll do Windows.
  * **src/main.c**: Executavel de linha de comando e suite de auto-teste.
  * **Makefile**: Automacao de compilacao compativel com MinGW-w64 (GCC).
* **src/Jade.Native**: Interoperabilidade C# gerenciada via P/Invoke com a biblioteca jade.dll e esent.dll.
* **src/Jade.Core**: Camada de dominio e servicos de alto nivel para emulacao do Active Directory.
* **tests/Jade.Tests**: Suite automatizada de testes transacionais e de estresse.

---

## Governanca e Memoria Tecnica

Consulte o arquivo [MEMORIA_COLETIVA.md](./MEMORIA_COLETIVA.md) para diretivas mandatorias e historico de revisoes.
