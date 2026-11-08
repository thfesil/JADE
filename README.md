# Jade: Jet Active Directory Emulator

Jade e um emulador autonomo, leve e de alta fidelidade de Controlador de Dominio e do motor de banco de dados do Microsoft Active Directory (NTDS.DIT), implementado diretamente sobre o motor nativo Jet Blue ESE (Extensible Storage Engine / esent.dll) do Windows com suporte completo ao protocolo de rede LDAPv3 e LDAPS (TLS 1.2 / 1.3).

---

## Novidades Recentes (Jade 1 - Rev 4)

* Implementacao do executavel autonomo de Controlador de Dominio emulado (**Jade.Server** / `jade-server.exe`), operando como um servidor Active Directory completo em rede.
* Suporte nativo ao protocolo binario ASN.1 / BER LDAP v3 (RFC 4511) cobrindo `BindRequest`, `SearchRequest` (incluindo descoberta `RootDSE`), `AddRequest`, `ModifyRequest`, `DelRequest`, `UnbindRequest` e `ExtendedRequest`.
* Camada criptografica TLS 1.2 / TLS 1.3 com geracao automatica de certificado X.509 RSA-2048 (`TlsCertificateManager`) compativel com o SChannel do Windows, suportando tanto **LDAPS implicito (porta 636 / 6360)** quanto elevacao dinamica de canal via **StartTLS (OID `1.3.6.1.4.1.1466.20037`)**.
* Politica de seguranca criptografica para senhas (`unicodePwd`): exigencia de canal protegido TLS (rejeitando tentativas em texto claro com erro oficial `DSID-0C090446` / `LDAP_UNWILLING_TO_PERFORM`), decodificacao do formato UTF-16LE delimitado por aspas duplas e armazenamento de hash HMAC-SHA256 com verificacao em tempo constante (`JadeSecurity`).
* Emissao de subcodigos de diagnostico autenticos do Active Directory durante autenticacao (`data 525` para usuario inexistente, `data 52e` para credenciais invalidas e `data 533` para conta desativada via UAC 514) e geracao binaria de `objectSid` (`S-1-5-21-...-RID`).
* Suite de testes xUnit ampliada com 100% de aprovacao (8 de 8 testes aprovados) e modo de auto-validacao de rede (`jade-server.exe --self-test`).

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
