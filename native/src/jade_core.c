#include "../include/jade.h"
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <objbase.h>

struct jade_db_context {
    JET_INSTANCE instance;
    JET_SESID    sesid;
    JET_DBID     dbid;
    char         db_path[MAX_PATH];
    char         base_dir[MAX_PATH];
    
    /* Column IDs for datatable */
    JET_COLUMNID col_dnt;
    JET_COLUMNID col_pdnt;
    JET_COLUMNID col_ncdnt;
    JET_COLUMNID col_class;
    JET_COLUMNID col_sam;
    JET_COLUMNID col_upn;
    JET_COLUMNID col_uac;
    JET_COLUMNID col_pwd;
    JET_COLUMNID col_guid;
    JET_COLUMNID col_sid;
    JET_COLUMNID col_is_deleted;
    JET_COLUMNID col_when_created;
    JET_COLUMNID col_when_changed;

    /* Column IDs for fsmo_table */
    JET_COLUMNID col_fsmo_current_rid;
    JET_COLUMNID col_fsmo_max_rid;
    JET_COLUMNID col_fsmo_block_size;
    JET_COLUMNID col_fsmo_exhaustion;
};

static int64_t get_current_filetime(void) {
    FILETIME ft;
    GetSystemTimeAsFileTime(&ft);
    ULARGE_INTEGER uli;
    uli.LowPart = ft.dwLowDateTime;
    uli.HighPart = ft.dwHighDateTime;
    return (int64_t)uli.QuadPart;
}

JADE_API int jade_system_init(void) {
    JET_ERR err = JetSetSystemParameterA(NULL, 0, JET_paramDatabasePageSize, 8192, NULL);
    if (err != JET_errSuccess && err != JET_errAlreadyInitialized) {
        return JADE_ERR_INIT_FAILED;
    }
    return JADE_OK;
}

JADE_API int jade_system_term(void) {
    return JADE_OK;
}

static JET_ERR bind_table_columns(jade_db_t* db) {
    JET_COLUMNDEF coldef;
    JET_ERR err;

    err = JetGetColumnInfoA(db->sesid, db->dbid, "datatable", "DNT", &coldef, sizeof(coldef), JET_ColInfo);
    if (err != JET_errSuccess) return err;
    db->col_dnt = coldef.columnid;

    JetGetColumnInfoA(db->sesid, db->dbid, "datatable", "PDNT", &coldef, sizeof(coldef), JET_ColInfo);
    db->col_pdnt = coldef.columnid;

    JetGetColumnInfoA(db->sesid, db->dbid, "datatable", "NCDNT", &coldef, sizeof(coldef), JET_ColInfo);
    db->col_ncdnt = coldef.columnid;

    JetGetColumnInfoA(db->sesid, db->dbid, "datatable", "ObjectClass", &coldef, sizeof(coldef), JET_ColInfo);
    db->col_class = coldef.columnid;

    JetGetColumnInfoA(db->sesid, db->dbid, "datatable", "sAMAccountName", &coldef, sizeof(coldef), JET_ColInfo);
    db->col_sam = coldef.columnid;

    JetGetColumnInfoA(db->sesid, db->dbid, "datatable", "userPrincipalName", &coldef, sizeof(coldef), JET_ColInfo);
    db->col_upn = coldef.columnid;

    JetGetColumnInfoA(db->sesid, db->dbid, "datatable", "userAccountControl", &coldef, sizeof(coldef), JET_ColInfo);
    db->col_uac = coldef.columnid;

    JetGetColumnInfoA(db->sesid, db->dbid, "datatable", "unicodePwd", &coldef, sizeof(coldef), JET_ColInfo);
    db->col_pwd = coldef.columnid;

    JetGetColumnInfoA(db->sesid, db->dbid, "datatable", "objectGUID", &coldef, sizeof(coldef), JET_ColInfo);
    db->col_guid = coldef.columnid;

    JetGetColumnInfoA(db->sesid, db->dbid, "datatable", "objectSid", &coldef, sizeof(coldef), JET_ColInfo);
    db->col_sid = coldef.columnid;

    JetGetColumnInfoA(db->sesid, db->dbid, "datatable", "isDeleted", &coldef, sizeof(coldef), JET_ColInfo);
    db->col_is_deleted = coldef.columnid;

    JetGetColumnInfoA(db->sesid, db->dbid, "datatable", "whenCreated", &coldef, sizeof(coldef), JET_ColInfo);
    db->col_when_created = coldef.columnid;

    JetGetColumnInfoA(db->sesid, db->dbid, "datatable", "whenChanged", &coldef, sizeof(coldef), JET_ColInfo);
    db->col_when_changed = coldef.columnid;

    JetGetColumnInfoA(db->sesid, db->dbid, "fsmo_table", "CurrentRid", &coldef, sizeof(coldef), JET_ColInfo);
    db->col_fsmo_current_rid = coldef.columnid;

    JetGetColumnInfoA(db->sesid, db->dbid, "fsmo_table", "MaxRid", &coldef, sizeof(coldef), JET_ColInfo);
    db->col_fsmo_max_rid = coldef.columnid;

    JetGetColumnInfoA(db->sesid, db->dbid, "fsmo_table", "BlockSize", &coldef, sizeof(coldef), JET_ColInfo);
    db->col_fsmo_block_size = coldef.columnid;

    JetGetColumnInfoA(db->sesid, db->dbid, "fsmo_table", "SimulateExhaustion", &coldef, sizeof(coldef), JET_ColInfo);
    db->col_fsmo_exhaustion = coldef.columnid;

    return JET_errSuccess;
}

static int create_schema(jade_db_t* db) {
    JET_TABLEID tableid;
    JET_COLUMNID colid;
    JET_COLUMNDEF coldef;
    JET_ERR err;

    /* 1. Create datatable */
    err = JetCreateTableA(db->sesid, db->dbid, "datatable", 128, 100, &tableid);
    if (err != JET_errSuccess) return JADE_ERR_TABLE_CREATE_FAILED;

    memset(&coldef, 0, sizeof(coldef));
    coldef.cbStruct = sizeof(coldef);
    coldef.coltyp = JET_coltypLong;
    coldef.grbit = JET_bitColumnAutoincrement;
    JetAddColumnA(db->sesid, tableid, "DNT", &coldef, NULL, 0, &colid);

    coldef.grbit = 0;
    coldef.coltyp = JET_coltypLong;
    JetAddColumnA(db->sesid, tableid, "PDNT", &coldef, NULL, 0, &colid);
    JetAddColumnA(db->sesid, tableid, "NCDNT", &coldef, NULL, 0, &colid);
    JetAddColumnA(db->sesid, tableid, "ObjectClass", &coldef, NULL, 0, &colid);
    JetAddColumnA(db->sesid, tableid, "userAccountControl", &coldef, NULL, 0, &colid);

    /* Text columns: sAMAccountName is indexed (JET_coltypText, max 255 bytes / 127 WCHARs) */
    coldef.coltyp = JET_coltypText;
    coldef.cp = 1200;
    coldef.cbMax = 255;
    JetAddColumnA(db->sesid, tableid, "sAMAccountName", &coldef, NULL, 0, &colid);

    /* userPrincipalName can be longer (JET_coltypLongText) */
    coldef.coltyp = JET_coltypLongText;
    coldef.cp = 1200;
    coldef.cbMax = 512;
    JetAddColumnA(db->sesid, tableid, "userPrincipalName", &coldef, NULL, 0, &colid);

    coldef.coltyp = JET_coltypLongBinary;
    coldef.cbMax = 512;
    JetAddColumnA(db->sesid, tableid, "unicodePwd", &coldef, NULL, 0, &colid);

    coldef.cbMax = 16;
    JetAddColumnA(db->sesid, tableid, "objectGUID", &coldef, NULL, 0, &colid);

    coldef.cbMax = 68;
    JetAddColumnA(db->sesid, tableid, "objectSid", &coldef, NULL, 0, &colid);

    coldef.coltyp = JET_coltypBit;
    coldef.cbMax = 1;
    JetAddColumnA(db->sesid, tableid, "isDeleted", &coldef, NULL, 0, &colid);

    coldef.coltyp = JET_coltypLongLong;
    coldef.cbMax = 8;
    JetAddColumnA(db->sesid, tableid, "whenCreated", &coldef, NULL, 0, &colid);
    JetAddColumnA(db->sesid, tableid, "whenChanged", &coldef, NULL, 0, &colid);

    /* Double null terminated index keys */
    const char dnt_key[] = "+DNT\0";
    JetCreateIndexA(db->sesid, tableid, "DNT_Index", JET_bitIndexPrimary | JET_bitIndexUnique, dnt_key, sizeof(dnt_key), 100);

    const char pdnt_key[] = "+PDNT\0+DNT\0";
    JetCreateIndexA(db->sesid, tableid, "PDNT_Index", 0, pdnt_key, sizeof(pdnt_key), 100);

    const char sam_key[] = "+sAMAccountName\0";
    JetCreateIndexA(db->sesid, tableid, "SAM_Index", JET_bitIndexUnique | JET_bitIndexIgnoreNull, sam_key, sizeof(sam_key), 100);

    const char guid_key[] = "+objectGUID\0";
    JetCreateIndexA(db->sesid, tableid, "GUID_Index", JET_bitIndexUnique | JET_bitIndexIgnoreNull, guid_key, sizeof(guid_key), 100);

    JetCloseTable(db->sesid, tableid);

    /* 2. Create fsmo_table */
    err = JetCreateTableA(db->sesid, db->dbid, "fsmo_table", 16, 100, &tableid);
    if (err != JET_errSuccess) return JADE_ERR_TABLE_CREATE_FAILED;

    memset(&coldef, 0, sizeof(coldef));
    coldef.cbStruct = sizeof(coldef);
    coldef.coltyp = JET_coltypLong;
    JetAddColumnA(db->sesid, tableid, "CurrentRid", &coldef, NULL, 0, &colid);
    JetAddColumnA(db->sesid, tableid, "MaxRid", &coldef, NULL, 0, &colid);
    JetAddColumnA(db->sesid, tableid, "BlockSize", &coldef, NULL, 0, &colid);

    coldef.coltyp = JET_coltypBit;
    JetAddColumnA(db->sesid, tableid, "SimulateExhaustion", &coldef, NULL, 0, &colid);

    JetPrepareUpdate(db->sesid, tableid, JET_prepInsert);
    uint32_t start_rid = 1000;
    uint32_t max_rid = 1000000000;
    uint32_t block_size = 500;
    unsigned char exhaustion = 0;

    JetGetColumnInfoA(db->sesid, db->dbid, "fsmo_table", "CurrentRid", &coldef, sizeof(coldef), JET_ColInfo);
    JetSetColumn(db->sesid, tableid, coldef.columnid, &start_rid, sizeof(start_rid), 0, NULL);
    JetGetColumnInfoA(db->sesid, db->dbid, "fsmo_table", "MaxRid", &coldef, sizeof(coldef), JET_ColInfo);
    JetSetColumn(db->sesid, tableid, coldef.columnid, &max_rid, sizeof(max_rid), 0, NULL);
    JetGetColumnInfoA(db->sesid, db->dbid, "fsmo_table", "BlockSize", &coldef, sizeof(coldef), JET_ColInfo);
    JetSetColumn(db->sesid, tableid, coldef.columnid, &block_size, sizeof(block_size), 0, NULL);
    JetGetColumnInfoA(db->sesid, db->dbid, "fsmo_table", "SimulateExhaustion", &coldef, sizeof(coldef), JET_ColInfo);
    JetSetColumn(db->sesid, tableid, coldef.columnid, &exhaustion, sizeof(exhaustion), 0, NULL);

    JetUpdate(db->sesid, tableid, NULL, 0, NULL);
    JetCloseTable(db->sesid, tableid);

    return JADE_OK;
}

JADE_API int jade_db_open(const char* base_dir, const char* db_name, bool create_if_missing, jade_db_t** out_db) {
    jade_system_init();

    jade_db_t* db = (jade_db_t*)calloc(1, sizeof(jade_db_t));
    if (!db) return JADE_ERR_INIT_FAILED;

    strncpy(db->base_dir, base_dir, MAX_PATH - 1);
    snprintf(db->db_path, MAX_PATH, "%s\\%s", base_dir, db_name);
    CreateDirectoryA(base_dir, NULL);

    char log_dir_path[MAX_PATH];
    snprintf(log_dir_path, MAX_PATH, "%s\\logs", base_dir);
    CreateDirectoryA(base_dir, NULL);
    CreateDirectoryA(log_dir_path, NULL);

    char log_dir_param[MAX_PATH];
    snprintf(log_dir_param, MAX_PATH, "%s\\logs\\", base_dir);

    JET_ERR err = JetCreateInstanceA(&db->instance, "JadeEngineInstance");
    if (err != JET_errSuccess) {
        free(db);
        return JADE_ERR_INIT_FAILED;
    }

    JetSetSystemParameterA(&db->instance, 0, JET_paramLogFilePath, 0, log_dir_param);
    JetSetSystemParameterA(&db->instance, 0, JET_paramSystemPath, 0, log_dir_param);
    JetSetSystemParameterA(&db->instance, 0, JET_paramTempPath, 0, log_dir_param);
    JetSetSystemParameterA(&db->instance, 0, JET_paramCircularLog, 1, NULL);

    err = JetInit(&db->instance);
    if (err != JET_errSuccess) {
        JetTerm(db->instance);
        free(db);
        return JADE_ERR_INIT_FAILED;
    }

    err = JetBeginSession(db->instance, &db->sesid, NULL, NULL);
    if (err != JET_errSuccess) {
        JetTerm(db->instance);
        free(db);
        return JADE_ERR_INIT_FAILED;
    }

    DWORD file_attr = GetFileAttributesA(db->db_path);
    bool exists = (file_attr != INVALID_FILE_ATTRIBUTES && !(file_attr & FILE_ATTRIBUTE_DIRECTORY));

    if (!exists) {
        if (!create_if_missing) {
            JetEndSession(db->sesid, 0);
            JetTerm(db->instance);
            free(db);
            return JADE_ERR_RECORD_NOT_FOUND;
        }

        err = JetCreateDatabaseA(db->sesid, db->db_path, NULL, &db->dbid, 0);
        if (err != JET_errSuccess) {
            JetEndSession(db->sesid, 0);
            JetTerm(db->instance);
            free(db);
            return JADE_ERR_DB_CREATE_FAILED;
        }

        int sc_err = create_schema(db);
        if (sc_err != JADE_OK) {
            JetCloseDatabase(db->sesid, db->dbid, 0);
            JetEndSession(db->sesid, 0);
            JetTerm(db->instance);
            free(db);
            return sc_err;
        }
    } else {
        err = JetAttachDatabaseA(db->sesid, db->db_path, 0);
        if (err != JET_errSuccess && err != JET_wrnDatabaseAttached) {
            JetEndSession(db->sesid, 0);
            JetTerm(db->instance);
            free(db);
            return JADE_ERR_DB_CREATE_FAILED;
        }

        err = JetOpenDatabaseA(db->sesid, db->db_path, NULL, &db->dbid, 0);
        if (err != JET_errSuccess) {
            JetEndSession(db->sesid, 0);
            JetTerm(db->instance);
            free(db);
            return JADE_ERR_DB_CREATE_FAILED;
        }
    }

    bind_table_columns(db);

    *out_db = db;
    return JADE_OK;
}

JADE_API int jade_db_close(jade_db_t* db) {
    if (!db) return JADE_OK;

    if (db->sesid) {
        if (db->dbid) {
            JetCloseDatabase(db->sesid, db->dbid, 0);
            JetDetachDatabaseA(db->sesid, db->db_path);
        }
        JetEndSession(db->sesid, 0);
    }
    if (db->instance) {
        JetTerm2(db->instance, JET_bitTermComplete);
    }
    free(db);
    return JADE_OK;
}

JADE_API int jade_add_object(jade_db_t* db, const jade_object_t* obj, uint32_t* out_dnt) {
    if (!db || !obj) return JADE_ERR_INIT_FAILED;

    JET_TABLEID tableid;
    JET_ERR err = JetOpenTableA(db->sesid, db->dbid, "datatable", NULL, 0, 0, &tableid);
    if (err != JET_errSuccess) return JADE_ERR_TABLE_CREATE_FAILED;

    JetBeginTransaction(db->sesid);

    err = JetPrepareUpdate(db->sesid, tableid, JET_prepInsert);
    if (err != JET_errSuccess) {
        JetRollback(db->sesid, 0);
        JetCloseTable(db->sesid, tableid);
        return JADE_ERR_TRANSACTION_FAILED;
    }

    JetSetColumn(db->sesid, tableid, db->col_pdnt, &obj->pdnt, sizeof(obj->pdnt), 0, NULL);
    JetSetColumn(db->sesid, tableid, db->col_ncdnt, &obj->ncdnt, sizeof(obj->ncdnt), 0, NULL);
    JetSetColumn(db->sesid, tableid, db->col_class, &obj->object_class, sizeof(obj->object_class), 0, NULL);
    JetSetColumn(db->sesid, tableid, db->col_uac, &obj->user_account_control, sizeof(obj->user_account_control), 0, NULL);

    if (wcslen(obj->sam_account_name) > 0) {
        unsigned long cb = (unsigned long)(wcslen(obj->sam_account_name) * sizeof(wchar_t));
        JetSetColumn(db->sesid, tableid, db->col_sam, obj->sam_account_name, cb, 0, NULL);
    }

    if (wcslen(obj->user_principal_name) > 0) {
        unsigned long cb = (unsigned long)(wcslen(obj->user_principal_name) * sizeof(wchar_t));
        JetSetColumn(db->sesid, tableid, db->col_upn, obj->user_principal_name, cb, 0, NULL);
    }

    if (obj->unicode_pwd_len > 0) {
        JetSetColumn(db->sesid, tableid, db->col_pwd, obj->unicode_pwd, obj->unicode_pwd_len, 0, NULL);
    }

    uint8_t final_guid[16];
    memcpy(final_guid, obj->object_guid, 16);
    bool is_zero_guid = true;
    for (int i = 0; i < 16; i++) {
        if (final_guid[i] != 0) {
            is_zero_guid = false;
            break;
        }
    }
    if (is_zero_guid) {
        CoCreateGuid((GUID*)final_guid);
    }
    JetSetColumn(db->sesid, tableid, db->col_guid, final_guid, 16, 0, NULL);

    if (obj->object_sid_len > 0) {
        JetSetColumn(db->sesid, tableid, db->col_sid, obj->object_sid, obj->object_sid_len, 0, NULL);
    }

    unsigned char is_del = obj->is_deleted ? 1 : 0;
    JetSetColumn(db->sesid, tableid, db->col_is_deleted, &is_del, sizeof(is_del), 0, NULL);

    int64_t now = get_current_filetime();
    int64_t created = obj->when_created ? obj->when_created : now;
    int64_t changed = obj->when_changed ? obj->when_changed : now;
    JetSetColumn(db->sesid, tableid, db->col_when_created, &created, sizeof(created), 0, NULL);
    JetSetColumn(db->sesid, tableid, db->col_when_changed, &changed, sizeof(changed), 0, NULL);

    unsigned char bookmark[256];
    unsigned long cbActualBookmark = 0;
    err = JetUpdate(db->sesid, tableid, bookmark, sizeof(bookmark), &cbActualBookmark);
    if (err == JET_errKeyDuplicate) {
        JetRollback(db->sesid, 0);
        JetCloseTable(db->sesid, tableid);
        return JADE_ERR_DUPLICATE_SAM;
    }
    if (err != JET_errSuccess) {
        JetRollback(db->sesid, 0);
        JetCloseTable(db->sesid, tableid);
        return JADE_ERR_TRANSACTION_FAILED;
    }

    /* Move to the newly inserted record to read its generated DNT */
    uint32_t dnt = 0;
    if (JetGotoBookmark(db->sesid, tableid, bookmark, cbActualBookmark) == JET_errSuccess) {
        unsigned long actual = 0;
        JetRetrieveColumn(db->sesid, tableid, db->col_dnt, &dnt, sizeof(dnt), &actual, 0, NULL);
    }
    if (out_dnt) *out_dnt = dnt;

    JetCommitTransaction(db->sesid, 0);
    JetCloseTable(db->sesid, tableid);

    return JADE_OK;
}

static void read_object_from_table(jade_db_t* db, JET_TABLEID tableid, jade_object_t* out_obj) {
    memset(out_obj, 0, sizeof(jade_object_t));
    unsigned long actual = 0;

    JetRetrieveColumn(db->sesid, tableid, db->col_dnt, &out_obj->dnt, sizeof(out_obj->dnt), &actual, 0, NULL);
    JetRetrieveColumn(db->sesid, tableid, db->col_pdnt, &out_obj->pdnt, sizeof(out_obj->pdnt), &actual, 0, NULL);
    JetRetrieveColumn(db->sesid, tableid, db->col_ncdnt, &out_obj->ncdnt, sizeof(out_obj->ncdnt), &actual, 0, NULL);
    JetRetrieveColumn(db->sesid, tableid, db->col_class, &out_obj->object_class, sizeof(out_obj->object_class), &actual, 0, NULL);
    JetRetrieveColumn(db->sesid, tableid, db->col_uac, &out_obj->user_account_control, sizeof(out_obj->user_account_control), &actual, 0, NULL);

    actual = 0;
    JetRetrieveColumn(db->sesid, tableid, db->col_sam, out_obj->sam_account_name, sizeof(out_obj->sam_account_name) - sizeof(wchar_t), &actual, 0, NULL);

    actual = 0;
    JetRetrieveColumn(db->sesid, tableid, db->col_upn, out_obj->user_principal_name, sizeof(out_obj->user_principal_name) - sizeof(wchar_t), &actual, 0, NULL);

    actual = 0;
    JetRetrieveColumn(db->sesid, tableid, db->col_pwd, out_obj->unicode_pwd, sizeof(out_obj->unicode_pwd), &actual, 0, NULL);
    out_obj->unicode_pwd_len = actual;

    actual = 0;
    JetRetrieveColumn(db->sesid, tableid, db->col_guid, out_obj->object_guid, 16, &actual, 0, NULL);

    actual = 0;
    JetRetrieveColumn(db->sesid, tableid, db->col_sid, out_obj->object_sid, sizeof(out_obj->object_sid), &actual, 0, NULL);
    out_obj->object_sid_len = actual;

    unsigned char is_del = 0;
    actual = 0;
    JetRetrieveColumn(db->sesid, tableid, db->col_is_deleted, &is_del, 1, &actual, 0, NULL);
    out_obj->is_deleted = (is_del != 0);

    JetRetrieveColumn(db->sesid, tableid, db->col_when_created, &out_obj->when_created, sizeof(out_obj->when_created), &actual, 0, NULL);
    JetRetrieveColumn(db->sesid, tableid, db->col_when_changed, &out_obj->when_changed, sizeof(out_obj->when_changed), &actual, 0, NULL);
}

JADE_API int jade_find_by_sam(jade_db_t* db, const wchar_t* sam, jade_object_t* out_obj) {
    if (!db || !sam || !out_obj) return JADE_ERR_INIT_FAILED;

    JET_TABLEID tableid;
    JET_ERR err = JetOpenTableA(db->sesid, db->dbid, "datatable", NULL, 0, 0, &tableid);
    if (err != JET_errSuccess) {
        return JADE_ERR_TABLE_CREATE_FAILED;
    }

    err = JetSetCurrentIndexA(db->sesid, tableid, "SAM_Index");
    if (err != JET_errSuccess) {
        JetCloseTable(db->sesid, tableid);
        return JADE_ERR_TABLE_CREATE_FAILED;
    }

    unsigned long cb = (unsigned long)(wcslen(sam) * sizeof(wchar_t));
    err = JetMakeKey(db->sesid, tableid, sam, cb, JET_bitNewKey);
    if (err != JET_errSuccess) {
        JetCloseTable(db->sesid, tableid);
        return JADE_ERR_RECORD_NOT_FOUND;
    }

    err = JetSeek(db->sesid, tableid, JET_bitSeekEQ);
    if (err != JET_errSuccess) {
        JetCloseTable(db->sesid, tableid);
        return JADE_ERR_RECORD_NOT_FOUND;
    }

    read_object_from_table(db, tableid, out_obj);
    JetCloseTable(db->sesid, tableid);

    return JADE_OK;
}

JADE_API int jade_find_by_dnt(jade_db_t* db, uint32_t dnt, jade_object_t* out_obj) {
    if (!db || !out_obj) return JADE_ERR_INIT_FAILED;

    JET_TABLEID tableid;
    JET_ERR err = JetOpenTableA(db->sesid, db->dbid, "datatable", NULL, 0, 0, &tableid);
    if (err != JET_errSuccess) return JADE_ERR_TABLE_CREATE_FAILED;

    err = JetSetCurrentIndexA(db->sesid, tableid, "DNT_Index");
    if (err != JET_errSuccess) {
        JetCloseTable(db->sesid, tableid);
        return JADE_ERR_TABLE_CREATE_FAILED;
    }

    JetMakeKey(db->sesid, tableid, &dnt, sizeof(dnt), JET_bitNewKey);

    err = JetSeek(db->sesid, tableid, JET_bitSeekEQ);
    if (err != JET_errSuccess) {
        JetCloseTable(db->sesid, tableid);
        return JADE_ERR_RECORD_NOT_FOUND;
    }

    read_object_from_table(db, tableid, out_obj);
    JetCloseTable(db->sesid, tableid);

    return JADE_OK;
}

JADE_API int jade_modify_uac(jade_db_t* db, uint32_t dnt, uint32_t new_uac) {
    if (!db) return JADE_ERR_INIT_FAILED;

    JET_TABLEID tableid;
    JET_ERR err = JetOpenTableA(db->sesid, db->dbid, "datatable", NULL, 0, 0, &tableid);
    if (err != JET_errSuccess) return JADE_ERR_TABLE_CREATE_FAILED;

    err = JetSetCurrentIndexA(db->sesid, tableid, "DNT_Index");
    JetMakeKey(db->sesid, tableid, &dnt, sizeof(dnt), JET_bitNewKey);

    err = JetSeek(db->sesid, tableid, JET_bitSeekEQ);
    if (err != JET_errSuccess) {
        JetCloseTable(db->sesid, tableid);
        return JADE_ERR_RECORD_NOT_FOUND;
    }

    JetBeginTransaction(db->sesid);
    JetPrepareUpdate(db->sesid, tableid, JET_prepReplace);
    JetSetColumn(db->sesid, tableid, db->col_uac, &new_uac, sizeof(new_uac), 0, NULL);
    int64_t now = get_current_filetime();
    JetSetColumn(db->sesid, tableid, db->col_when_changed, &now, sizeof(now), 0, NULL);
    JetUpdate(db->sesid, tableid, NULL, 0, NULL);
    JetCommitTransaction(db->sesid, 0);

    JetCloseTable(db->sesid, tableid);
    return JADE_OK;
}

JADE_API int jade_set_password(jade_db_t* db, uint32_t dnt, const uint8_t* pwd_bytes, uint32_t pwd_len) {
    if (!db || !pwd_bytes) return JADE_ERR_INIT_FAILED;

    JET_TABLEID tableid;
    JET_ERR err = JetOpenTableA(db->sesid, db->dbid, "datatable", NULL, 0, 0, &tableid);
    if (err != JET_errSuccess) return JADE_ERR_TABLE_CREATE_FAILED;

    err = JetSetCurrentIndexA(db->sesid, tableid, "DNT_Index");
    JetMakeKey(db->sesid, tableid, &dnt, sizeof(dnt), JET_bitNewKey);

    err = JetSeek(db->sesid, tableid, JET_bitSeekEQ);
    if (err != JET_errSuccess) {
        JetCloseTable(db->sesid, tableid);
        return JADE_ERR_RECORD_NOT_FOUND;
    }

    JetBeginTransaction(db->sesid);
    JetPrepareUpdate(db->sesid, tableid, JET_prepReplace);
    JetSetColumn(db->sesid, tableid, db->col_pwd, pwd_bytes, pwd_len, 0, NULL);
    int64_t now = get_current_filetime();
    JetSetColumn(db->sesid, tableid, db->col_when_changed, &now, sizeof(now), 0, NULL);
    JetUpdate(db->sesid, tableid, NULL, 0, NULL);
    JetCommitTransaction(db->sesid, 0);

    JetCloseTable(db->sesid, tableid);
    return JADE_OK;
}

JADE_API int jade_delete_object(jade_db_t* db, uint32_t dnt, bool tombstone) {
    if (!db) return JADE_ERR_INIT_FAILED;

    JET_TABLEID tableid;
    JET_ERR err = JetOpenTableA(db->sesid, db->dbid, "datatable", NULL, 0, 0, &tableid);
    if (err != JET_errSuccess) return JADE_ERR_TABLE_CREATE_FAILED;

    err = JetSetCurrentIndexA(db->sesid, tableid, "DNT_Index");
    JetMakeKey(db->sesid, tableid, &dnt, sizeof(dnt), JET_bitNewKey);

    err = JetSeek(db->sesid, tableid, JET_bitSeekEQ);
    if (err != JET_errSuccess) {
        JetCloseTable(db->sesid, tableid);
        return JADE_ERR_RECORD_NOT_FOUND;
    }

    JetBeginTransaction(db->sesid);
    if (tombstone) {
        /* Active Directory tombstoning behavior: mark isDeleted=1 and rename sAMAccountName with tombstone prefix */
        JetPrepareUpdate(db->sesid, tableid, JET_prepReplace);
        unsigned char is_del = 1;
        JetSetColumn(db->sesid, tableid, db->col_is_deleted, &is_del, 1, 0, NULL);

        wchar_t old_sam[256] = {0};
        unsigned long actual = 0;
        JetRetrieveColumn(db->sesid, tableid, db->col_sam, old_sam, sizeof(old_sam) - sizeof(wchar_t), &actual, 0, NULL);

        wchar_t tombstone_sam[256];
        swprintf(tombstone_sam, 256, L"%ls\x0ADEL:%u", old_sam, dnt);
        unsigned long cb = (unsigned long)(wcslen(tombstone_sam) * sizeof(wchar_t));
        JetSetColumn(db->sesid, tableid, db->col_sam, tombstone_sam, cb, 0, NULL);

        int64_t now = get_current_filetime();
        JetSetColumn(db->sesid, tableid, db->col_when_changed, &now, sizeof(now), 0, NULL);
        JetUpdate(db->sesid, tableid, NULL, 0, NULL);
    } else {
        JetDelete(db->sesid, tableid);
    }
    JetCommitTransaction(db->sesid, 0);

    JetCloseTable(db->sesid, tableid);
    return JADE_OK;
}

JADE_API int jade_allocate_rid(jade_db_t* db, uint32_t* out_rid) {
    if (!db || !out_rid) return JADE_ERR_INIT_FAILED;

    JET_TABLEID tableid;
    JET_ERR err = JetOpenTableA(db->sesid, db->dbid, "fsmo_table", NULL, 0, 0, &tableid);
    if (err != JET_errSuccess) return JADE_ERR_TABLE_CREATE_FAILED;

    err = JetMove(db->sesid, tableid, JET_MoveFirst, 0);
    if (err != JET_errSuccess) {
        JetCloseTable(db->sesid, tableid);
        return JADE_ERR_RECORD_NOT_FOUND;
    }

    unsigned char exhaustion = 0;
    unsigned long actual = 0;
    JetRetrieveColumn(db->sesid, tableid, db->col_fsmo_exhaustion, &exhaustion, 1, &actual, 0, NULL);
    if (exhaustion) {
        JetCloseTable(db->sesid, tableid);
        return JADE_ERR_RID_EXHAUSTED; /* Returns Win32 8209 / 00002011 */
    }

    uint32_t current_rid = 0;
    JetRetrieveColumn(db->sesid, tableid, db->col_fsmo_current_rid, &current_rid, sizeof(current_rid), &actual, 0, NULL);

    uint32_t next_rid = current_rid + 1;

    JetBeginTransaction(db->sesid);
    JetPrepareUpdate(db->sesid, tableid, JET_prepReplace);
    JetSetColumn(db->sesid, tableid, db->col_fsmo_current_rid, &next_rid, sizeof(next_rid), 0, NULL);
    JetUpdate(db->sesid, tableid, NULL, 0, NULL);
    JetCommitTransaction(db->sesid, 0);

    JetCloseTable(db->sesid, tableid);

    *out_rid = current_rid;
    return JADE_OK;
}

JADE_API int jade_set_rid_exhaustion_simulation(jade_db_t* db, bool active) {
    if (!db) return JADE_ERR_INIT_FAILED;

    JET_TABLEID tableid;
    JET_ERR err = JetOpenTableA(db->sesid, db->dbid, "fsmo_table", NULL, 0, 0, &tableid);
    if (err != JET_errSuccess) return JADE_ERR_TABLE_CREATE_FAILED;

    err = JetMove(db->sesid, tableid, JET_MoveFirst, 0);
    if (err != JET_errSuccess) {
        JetCloseTable(db->sesid, tableid);
        return JADE_ERR_RECORD_NOT_FOUND;
    }

    JetBeginTransaction(db->sesid);
    JetPrepareUpdate(db->sesid, tableid, JET_prepReplace);
    unsigned char val = active ? 1 : 0;
    JetSetColumn(db->sesid, tableid, db->col_fsmo_exhaustion, &val, 1, 0, NULL);
    JetUpdate(db->sesid, tableid, NULL, 0, NULL);
    JetCommitTransaction(db->sesid, 0);

    JetCloseTable(db->sesid, tableid);
    return JADE_OK;
}

JADE_API int jade_get_rid_state(jade_db_t* db, jade_rid_state_t* out_state) {
    if (!db || !out_state) return JADE_ERR_INIT_FAILED;

    JET_TABLEID tableid;
    JET_ERR err = JetOpenTableA(db->sesid, db->dbid, "fsmo_table", NULL, 0, 0, &tableid);
    if (err != JET_errSuccess) return JADE_ERR_TABLE_CREATE_FAILED;

    err = JetMove(db->sesid, tableid, JET_MoveFirst, 0);
    if (err != JET_errSuccess) {
        JetCloseTable(db->sesid, tableid);
        return JADE_ERR_RECORD_NOT_FOUND;
    }

    unsigned long actual = 0;
    JetRetrieveColumn(db->sesid, tableid, db->col_fsmo_current_rid, &out_state->current_rid, sizeof(out_state->current_rid), &actual, 0, NULL);
    JetRetrieveColumn(db->sesid, tableid, db->col_fsmo_max_rid, &out_state->max_rid, sizeof(out_state->max_rid), &actual, 0, NULL);
    JetRetrieveColumn(db->sesid, tableid, db->col_fsmo_block_size, &out_state->block_size, sizeof(out_state->block_size), &actual, 0, NULL);

    unsigned char ex = 0;
    JetRetrieveColumn(db->sesid, tableid, db->col_fsmo_exhaustion, &ex, 1, &actual, 0, NULL);
    out_state->simulate_exhaustion = (ex != 0);

    JetCloseTable(db->sesid, tableid);
    return JADE_OK;
}

JADE_API int jade_count_objects(jade_db_t* db, uint32_t* out_count) {
    if (!db || !out_count) return JADE_ERR_INIT_FAILED;

    JET_TABLEID tableid;
    JET_ERR err = JetOpenTableA(db->sesid, db->dbid, "datatable", NULL, 0, 0, &tableid);
    if (err != JET_errSuccess) return JADE_ERR_TABLE_CREATE_FAILED;

    uint32_t count = 0;
    err = JetMove(db->sesid, tableid, JET_MoveFirst, 0);
    while (err == JET_errSuccess) {
        count++;
        err = JetMove(db->sesid, tableid, JET_MoveNext, 0);
    }

    JetCloseTable(db->sesid, tableid);
    *out_count = count;
    return JADE_OK;
}
