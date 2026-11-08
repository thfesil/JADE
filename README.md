# Jade: Jet Active Directory Emulator

Jade e um emulador autonomo, leve e de alta fidelidade de Controlador de Dominio e do motor de banco de dados do Microsoft Active Directory (NTDS.DIT), implementado diretamente sobre o motor nativo Jet Blue ESE (Extensible Storage Engine / esent.dll) do Windows com suporte completo ao protocolo de rede LDAPv3 e LDAPS (TLS 1.2 / 1.3).

---

## Novidades Recentes (Jade 1 - Rev 5)

* Implementacao de **Console Administrativo Interativo (`JADE-DC>`)** diretamente no executavel `jade-server.exe`, permitindo gerenciar o Controlador de Dominio em tempo real via terminal (`user create`, `user list`, `user show`, `user enable`, `user disable`, `user passwd`, `user role`, `user auth`, `user delete`, `rid exhaust on|off`, `status`, `help` e `quit`).
* Arquitetura de **Controle de Acesso Baseado em Papeis (RBAC)** com **4 Niveis de Acesso Corporativos** persistidos nativamente no `ntds.dit` e aplicados sobre todas as sessoes LDAP/LDAPS:
  * **DomainUser (`0` - `CN=Domain Users`, RID `513`)**: Autentica (`BindRequest`), realiza buscas (`SearchRequest`) e altera exclusivamente a propria senha (`unicodePwd`). Bloqueado ao tentar criar, desativar ou excluir contas.
  * **SecurityAuditor (`1` - `CN=Guests`, RID `546`)**: Acesso estritamente de leitura (`BindRequest` e `SearchRequest`) voltado para servicos de auditoria e SIEM. Qualquer operacao de escrita (`Add`, `Modify`, `Delete`) e rejeitada com codigo LDAP `50` (`LDAP_INSUFFICIENT_ACCESS_RIGHTS`).
  * **AccountOperator (`2` - `CN=Account Operators`, RID `548`)**: Permissao delegada para criar, ativar, desativar, redefinir senhas e excluir contas de `DomainUser`, `SecurityAuditor` e `AccountOperator`, porém bloqueado pelo **AdminSDHolder** ao tentar criar ou modificar contas `DomainAdmin`.
  * **DomainAdmin (`3` - `CN=Domain Admins`, RID `512`, `adminCount=1`)**: Controle administrativo irrestrito sobre o Controlador de Dominio e protegido contra escalacao de privilegios por **AdminSDHolder** (`DSID-03150E49`).
* Suite de testes xUnit ampliada com 100% de aprovacao (9 de 9 testes aprovados) e auto-teste expandido (`jade-server.exe --self-test` com 9 etapas).

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
