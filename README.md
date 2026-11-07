# Jade: Jet Active Directory Emulator

Jade e um emulador autonomo, leve e de alta fidelidade do motor de banco de dados do Microsoft Active Directory (NTDS.DIT), implementado diretamente sobre o motor nativo Jet Blue ESE (Extensible Storage Engine / esent.dll) do Windows.

---

## Novidades Recentes (Jade 1 - Rev 1)

* Criacao da solucao independente com arquitetura dividida em Jade.Native, Jade.Core e Jade.Tests.
* Estabelecimento da governanca tecnica e diretrizes mandatorias em MEMORIA_COLETIVA.md.
* Suporte nativo a persistencia ESE via esent.dll sem necessidade de licenca Windows Server ou maquinas virtuais.

---

## Estrutura da Solucao

* **src/Jade.Native**: Interoperabilidade de alta vazao com as APIs C da biblioteca Win32 esent.dll.
* **src/Jade.Core**: Modelagem de schema do Active Directory (tabela datatable, DNT, PDNT, atributos LDAP, ciclo de vida de contas e pools de RIDs).
* **tests/Jade.Tests**: Suite automatizada de testes transacionais e de estresse.

---

## Governanca e Memoria Tecnica

Consulte o arquivo [MEMORIA_COLETIVA.md](./MEMORIA_COLETIVA.md) para diretivas mandatorias e historico de revisoes.
