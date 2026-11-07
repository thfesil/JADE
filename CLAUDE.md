# DIRETRIZES DE ENGENHARIA PARA ASSISTENTES DE IA (JADE)

## Memoria Coletiva do Projeto Jade
Este projeto adota como documento central de governanca tecnica:
[MEMORIA_COLETIVA.md](./MEMORIA_COLETIVA.md)

### Diretivas Mandatorias:
1. Leitura Obrigatoria: Consulte MEMORIA_COLETIVA.md antes de propor ou executar qualquer alteracao no codigo.
2. Versionamento por Revisao: Seguir rigorosamente o padrao Jade 1 (Rev 1), Jade 1 (Rev 2)...
3. Isolamento de Familias em Branches: O branch main destina-se exclusivamente a versao ativa em producao.
4. Sincronizacao Imediata de Correcoes: Toda correcao de bug ou melhoria funcional deve ser commitada e enviada imediatamente para o repositorio.
5. Rigor de Profissionalismo: E terminantemente proibido o uso de travessoes (em-dash ou en-dash), emojis, girias ou informalidades em codigos, documentacoes ou commits.
6. Registro de Engenharia: Apos concluir alteracoes relevantes, documente a intervencao na secao Registro de Execucao e Historico de Engenharia ao final de MEMORIA_COLETIVA.md.
7. Preservacao Historica: Conserve integralmente os registros precedentes.
8. Resolucao Dinamica de Caminhos: Utilizar exclusivamente caminhos dinamicos com base em AppContext.BaseDirectory.
9. Seguranca e Privacidade: Nao registrar credenciais reais, chaves privadas ou dados pessoais.
10. Atualizacao do README.md: A cada entrega, atualizar a secao Novidades Recentes do README.md.
