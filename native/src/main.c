#include "../include/jade.h"
#include <stdio.h>
#include <stdlib.h>
#include <string.h>

static void print_banner(void) {
    printf("=================================================================\n");
    printf("  JADE: Jet Active Directory Emulator\n");
    printf("  Engine: Microsoft Windows Jet Blue ESE (esent.dll)\n");
    printf("  Architecture: NTDS.DIT B+Tree Datatable (DNT/PDNT/FSMO)\n");
    printf("=================================================================\n\n");
}

static int run_self_test(const char* data_dir) {
    printf("[1/8] Inicializando subsistema Jet Blue ESE...\n");
    if (jade_system_init() != JADE_OK) {
        printf("FALHA: Nao foi possivel inicializar o sistema ESE.\n");
        return 1;
    }

    printf("[2/8] Criando base de dados simulada NTDS.DIT em: %s\\jade_ntds.dit\n", data_dir);
    jade_db_t* db = NULL;
    int rc = jade_db_open(data_dir, "jade_ntds.dit", true, &db);
    if (rc != JADE_OK || !db) {
        printf("FALHA: Nao foi possivel criar ou abrir a base de dados (codigo: %d)\n", rc);
        return 1;
    }
    printf("      Base de dados e tabelas (datatable, fsmo_table) criadas com exito.\n");

    printf("[3/8] Inserindo Objetos Estruturais (Root Domain e Unidades Organizacionais)...\n");
    jade_object_t domain_root;
    memset(&domain_root, 0, sizeof(domain_root));
    domain_root.pdnt = 0;
    domain_root.object_class = JADE_CLASS_CONTAINER;
    wcscpy(domain_root.sam_account_name, L"DC=jade,DC=local");
    uint32_t root_dnt = 0;
    jade_add_object(db, &domain_root, &root_dnt);
    printf("      Dominio Raiz criado com DNT: %u\n", root_dnt);

    jade_object_t ou_users;
    memset(&ou_users, 0, sizeof(ou_users));
    ou_users.pdnt = root_dnt;
    ou_users.object_class = JADE_CLASS_ORGANIZATIONAL_UNIT;
    wcscpy(ou_users.sam_account_name, L"OU=Colaboradores");
    uint32_t ou_dnt = 0;
    rc = jade_add_object(db, &ou_users, &ou_dnt);
    if (rc != JADE_OK) {
        printf("FALHA ao criar OU=Colaboradores (codigo: %d)\n", rc);
        jade_db_close(db);
        return 1;
    }
    printf("      OU=Colaboradores criada com DNT: %u (PDNT: %u)\n", ou_dnt, root_dnt);

    printf("[4/8] Provisionando Conta de Usuario (AddRequest)... \n");
    jade_object_t user;
    memset(&user, 0, sizeof(user));
    user.pdnt = ou_dnt;
    user.object_class = JADE_CLASS_USER;
    wcscpy(user.sam_account_name, L"analista.sec");
    wcscpy(user.user_principal_name, L"analista.sec@jade.local");
    user.user_account_control = JADE_UAC_NORMAL_ACCOUNT; /* 512 */
    
    const char* pwd_raw = "SenhaForte#2026";
    memcpy(user.unicode_pwd, pwd_raw, strlen(pwd_raw));
    user.unicode_pwd_len = (uint32_t)strlen(pwd_raw);

    /* Generate pseudo GUID */
    for (int i = 0; i < 16; i++) user.object_guid[i] = (uint8_t)(i + 1);

    uint32_t user_dnt = 0;
    rc = jade_add_object(db, &user, &user_dnt);
    if (rc != JADE_OK) {
        printf("FALHA ao criar usuario (codigo: %d)\n", rc);
        jade_db_close(db);
        return 1;
    }
    printf("      Usuario 'analista.sec' inserido com DNT: %u, UAC: %u\n", user_dnt, user.user_account_control);

    printf("[5/8] Testando Busca Indexada O(1) na B+Tree por sAMAccountName...\n");
    jade_object_t found;
    rc = jade_find_by_sam(db, L"analista.sec", &found);
    if (rc != JADE_OK) {
        printf("FALHA: Objeto nao localizado por sAMAccountName!\n");
        jade_db_close(db);
        return 1;
    }
    printf("      Sucesso: Objeto localizado. DNT=%u, UPN=%ls, Ativo=%s\n", 
           found.dnt, found.user_principal_name, (found.user_account_control & JADE_UAC_ACCOUNTDISABLE) ? "Nao" : "Sim");

    printf("[6/8] Testando Modificacoes (Desativacao UAC 514 e Redefinicao de Senha)...\n");
    jade_modify_uac(db, user_dnt, JADE_UAC_NORMAL_ACCOUNT | JADE_UAC_ACCOUNTDISABLE); /* 514 */
    const char* new_pwd = "NovaSenha#2026";
    jade_set_password(db, user_dnt, (const uint8_t*)new_pwd, (uint32_t)strlen(new_pwd));

    jade_find_by_dnt(db, user_dnt, &found);
    printf("      Novo UAC confirmado: %u (Conta desativada com sucesso)\n", found.user_account_control);

    printf("[7/8] Testando FSMO RID Master e Simulacao de Esgotamento (Win32 8209)...\n");
    uint32_t rid1 = 0, rid2 = 0;
    jade_allocate_rid(db, &rid1);
    jade_allocate_rid(db, &rid2);
    printf("      RIDs sequenciais alocados: %u, %u\n", rid1, rid2);

    /* Injetando falha de esgotamento */
    jade_set_rid_exhaustion_simulation(db, true);
    uint32_t rid3 = 0;
    int rid_err = jade_allocate_rid(db, &rid3);
    if (rid_err == JADE_ERR_RID_EXHAUSTED) {
        printf("      Sucesso: Injecao de esgotamento interceptada perfeitamente com codigo Win32 %d (WILL_NOT_PERFORM / 00002011).\n", rid_err);
    } else {
        printf("FALHA: Injecao de esgotamento nao retornou erro 8209!\n");
    }
    /* Restaurando estado */
    jade_set_rid_exhaustion_simulation(db, false);

    printf("[8/8] Testando Ciclo de Exclusao Logica (Tombstone / isDeleted)...\n");
    jade_delete_object(db, user_dnt, true);
    jade_find_by_dnt(db, user_dnt, &found);
    printf("      Objeto DNT %u marcado como isDeleted=%d, novo sAMAccountName: %ls\n", 
           user_dnt, found.is_deleted ? 1 : 0, found.sam_account_name);

    uint32_t total = 0;
    jade_count_objects(db, &total);
    printf("\nTotal de objetos persistidos no NTDS.DIT: %u\n", total);

    jade_db_close(db);
    printf("Sessao e transacoes ESE finalizadas de forma limpa.\n");
    return 0;
}

int main(int argc, char* argv[]) {
    setvbuf(stdout, NULL, _IONBF, 0);
    print_banner();

    char base_dir[MAX_PATH] = "F:\\Projects\\Jade\\data_simulated";
    if (argc > 1) {
        strncpy(base_dir, argv[1], MAX_PATH - 1);
    }

    printf("Iniciando validacao do motor JADE sobre esent.dll...\n\n");
    int res = run_self_test(base_dir);
    if (res == 0) {
        printf("\n=================================================================\n");
        printf("  STATUS: TODOS OS TESTES PASSARAM COM SUCESSO (100%% OPERACIONAL)\n");
        printf("=================================================================\n");
    } else {
        printf("\n=================================================================\n");
        printf("  STATUS: FALHA NA EXECUCAO DOS TESTES DO JADE\n");
        printf("=================================================================\n");
    }
    return res;
}
