# Jade: Jet Active Directory Emulator

Jade e um emulador autonomo, leve e de alta fidelidade de Controlador de Dominio e do motor de banco de dados do Microsoft Active Directory (NTDS.DIT), implementado diretamente sobre o motor nativo Jet Blue ESE (Extensible Storage Engine / esent.dll) do Windows com suporte completo ao protocolo de rede LDAPv3 e LDAPS (TLS 1.2 / 1.3).

---

## Novidades Recentes (Jade 1 - Rev 6)

* Implementacao de **Grupos de Seguranca do Active Directory (`objectClass: group`)** e comandos da familia `group` no Console Administrativo Interativo (`JADE-DC>`):
  * `group create <nome>`: Cria grupos de seguranca com alocacao dinamica de RID e SID no NTDS.DIT (`S-1-5-21-...-RID`), `groupType = -2147483646` (Global Security Group).
  * `group list`: Lista todos os grupos de seguranca ativos e contagem de membros associados.
  * `group show <nome>`: Exibe atributos detalhados do grupo, GUID, SID e relacao de membros.
  * `group add-member <grupo> <usuario>`: Associa um usuario corporativo como membro do grupo.
  * `group remove-member <grupo> <usuario>`: Remove a associacao de um usuario ao grupo.
  * `group delete <nome>`: Exclui o grupo e move o objeto para o container Tombstone (`isDeleted = 1`).
* Suporte completo no servidor **LDAP/LDAPS** para criacao, consulta e manipulacao de grupos e associacoes:
  * Atributo multi-valor `member` nos objetos de grupo contendo os Distinguished Names (DNs) dos membros.
  * Atributo multi-valor `memberOf` nos objetos de usuario contendo os DNs dos grupos aos quais o usuario pertence.
  * Provisionamento automatico dos grupos corporativos padrao do Active Directory: `Domain Admins` (RID 512), `Domain Users` (RID 513), `Account Operators` (RID 548) e `Security Auditors` (RID 546).
* Suite de testes xUnit expandida cobrindo persistencia de grupos, membros e resolucao de protocolo LDAPv3/LDAPS (10 de 10 testes aprovados).

---

## Estrutura da Solucao

* **native/**: Motor central de alto desempenho desenvolvido em C ANSI/C99.
  * **include/jade.h**: Contratos publicos da API C, estruturas e definicoes de schema.
  * **src/jade_core.c**: Implementacao direta sobre a biblioteca esent.dll do Windows.
  * **src/main.c**: Executavel de linha de comando e suite de auto-teste nativo.
  * **Makefile**: Automacao de compilacao compativel com MinGW-w64 (GCC).
* **src/Jade.Native**: Interoperabilidade C# gerenciada via P/Invoke com a biblioteca jade.dll e esent.dll.
* **src/Jade.Core**: Camada de dominio, criptografia de senhas e SIDs (`JadeSecurity`) e controle transacional (`JadeDatabase`).
* **src/Jade.Server**: Executavel do Controlador de Dominio emulado (`jade-server.exe`), codec ASN.1/BER (`BerCodec`), servidor TCP/TLS (`JadeDomainControllerServer`) e cliente LDAP/LDAPS (`JadeLdapClient`).
* **tests/Jade.Tests**: Suite automatizada de testes xUnit cobrindo persistencia ESE, criptografia e operacoes de rede LDAP/LDAPS.

---

## Governanca e Memoria Tecnica

Consulte o arquivo [MEMORIA_COLETIVA.md](./MEMORIA_COLETIVA.md) para diretivas mandatorias e historico de revisoes.
