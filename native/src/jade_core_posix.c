#ifndef _WIN32

#define _POSIX_C_SOURCE 200809L
#include "../include/jade.h"
#include <errno.h>
#include <fcntl.h>
#include <limits.h>
#include <pthread.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <sys/stat.h>
#include <time.h>
#include <unistd.h>

#define JADE_DB_MAGIC "JADEDB01"
#define JADE_DB_FORMAT_VERSION 1u
#define JADE_FILETIME_EPOCH_DIFF 11644473600LL

typedef struct {
    char magic[8];
    uint32_t version;
    uint32_t next_dnt;
    jade_rid_state_t rid_state;
    uint32_t record_count;
} jade_file_header_t;

struct jade_db_context {
    pthread_mutex_t lock;
    char base_dir[PATH_MAX];
    char db_path[PATH_MAX];
    uint32_t next_dnt;
    jade_rid_state_t rid_state;
    jade_object_t* records;
    uint32_t record_count;
    uint32_t record_capacity;
};

static int64_t get_current_filetime(void) {
    struct timespec ts;
    if (clock_gettime(CLOCK_REALTIME, &ts) != 0) {
        return 0;
    }
    return ((int64_t)ts.tv_sec + JADE_FILETIME_EPOCH_DIFF) * 10000000LL + (int64_t)(ts.tv_nsec / 100);
}

static size_t u16_len(const jade_wchar_t* s, size_t max_len) {
    size_t i = 0;
    while (i < max_len && s[i] != 0) {
        i++;
    }
    return i;
}

static uint16_t u16_ascii_lower(uint16_t c) {
    if (c >= 'A' && c <= 'Z') {
        return (uint16_t)(c + ('a' - 'A'));
    }
    return c;
}

static int u16_icmp(const jade_wchar_t* a, const jade_wchar_t* b, size_t max_len) {
    for (size_t i = 0; i < max_len; i++) {
        uint16_t ca = u16_ascii_lower(a[i]);
        uint16_t cb = u16_ascii_lower(b[i]);
        if (ca != cb) {
            return (ca < cb) ? -1 : 1;
        }
        if (ca == 0) {
            return 0;
        }
    }
    return 0;
}

static void generate_random_guid(uint8_t out_guid[16]) {
    int fd = open("/dev/urandom", O_RDONLY);
    if (fd >= 0) {
        ssize_t n = read(fd, out_guid, 16);
        close(fd);
        if (n == 16) {
            out_guid[6] = (uint8_t)((out_guid[6] & 0x0F) | 0x40);
            out_guid[8] = (uint8_t)((out_guid[8] & 0x3F) | 0x80);
            return;
        }
    }

    static uint32_t fallback_seq = 1;
    struct timespec ts;
    clock_gettime(CLOCK_REALTIME, &ts);
    uint64_t t = (uint64_t)ts.tv_sec * 1000000000ULL + (uint64_t)ts.tv_nsec;
    uint32_t pid = (uint32_t)getpid();
    uint32_t seq = __sync_fetch_and_add(&fallback_seq, 1);
    memcpy(out_guid, &t, 8);
    memcpy(out_guid + 8, &pid, 4);
    memcpy(out_guid + 12, &seq, 4);
    out_guid[6] = (uint8_t)((out_guid[6] & 0x0F) | 0x40);
    out_guid[8] = (uint8_t)((out_guid[8] & 0x3F) | 0x80);
}

static bool is_guid_empty(const uint8_t guid[16]) {
    for (int i = 0; i < 16; i++) {
        if (guid[i] != 0) return false;
    }
    return true;
}

static int mkdir_p(const char* path) {
    char tmp[PATH_MAX];
    size_t len = strlen(path);
    if (len == 0 || len >= sizeof(tmp)) {
        return -1;
    }
    memcpy(tmp, path, len + 1);
    if (tmp[len - 1] == '/') {
        tmp[len - 1] = '\0';
    }
    for (char* p = tmp + 1; *p; p++) {
        if (*p == '/') {
            *p = '\0';
            if (mkdir(tmp, 0755) != 0 && errno != EEXIST) {
                return -1;
            }
            *p = '/';
        }
    }
    if (mkdir(tmp, 0755) != 0 && errno != EEXIST) {
        return -1;
    }
    return 0;
}

static int flush_db(jade_db_t* db) {
    char tmp_path[PATH_MAX];
    int written = snprintf(tmp_path, sizeof(tmp_path), "%s.tmp", db->db_path);
    if (written < 0 || (size_t)written >= sizeof(tmp_path)) {
        return JADE_ERR_TRANSACTION_FAILED;
    }

    FILE* fp = fopen(tmp_path, "wb");
    if (!fp) {
        return JADE_ERR_TRANSACTION_FAILED;
    }

    jade_file_header_t hdr;
    memset(&hdr, 0, sizeof(hdr));
    memcpy(hdr.magic, JADE_DB_MAGIC, 8);
    hdr.version = JADE_DB_FORMAT_VERSION;
    hdr.next_dnt = db->next_dnt;
    hdr.rid_state = db->rid_state;
    hdr.record_count = db->record_count;

    if (fwrite(&hdr, sizeof(hdr), 1, fp) != 1) {
        fclose(fp);
        unlink(tmp_path);
        return JADE_ERR_TRANSACTION_FAILED;
    }

    if (db->record_count > 0) {
        if (fwrite(db->records, sizeof(jade_object_t), db->record_count, fp) != db->record_count) {
            fclose(fp);
            unlink(tmp_path);
            return JADE_ERR_TRANSACTION_FAILED;
        }
    }

    fflush(fp);
    fsync(fileno(fp));
    fclose(fp);

    if (rename(tmp_path, db->db_path) != 0) {
        unlink(tmp_path);
        return JADE_ERR_TRANSACTION_FAILED;
    }
    return JADE_OK;
}

static int load_db(jade_db_t* db) {
    FILE* fp = fopen(db->db_path, "rb");
    if (!fp) {
        return JADE_ERR_DB_CREATE_FAILED;
    }

    jade_file_header_t hdr;
    if (fread(&hdr, sizeof(hdr), 1, fp) != 1) {
        fclose(fp);
        return JADE_ERR_DB_CREATE_FAILED;
    }

    if (memcmp(hdr.magic, JADE_DB_MAGIC, 8) != 0 || hdr.version != JADE_DB_FORMAT_VERSION) {
        fclose(fp);
        return JADE_ERR_DB_CREATE_FAILED;
    }

    db->next_dnt = hdr.next_dnt;
    db->rid_state = hdr.rid_state;
    db->record_count = hdr.record_count;
    db->record_capacity = (hdr.record_count > 16) ? hdr.record_count : 16;
    db->records = (jade_object_t*)calloc(db->record_capacity, sizeof(jade_object_t));
    if (!db->records) {
        fclose(fp);
        return JADE_ERR_INIT_FAILED;
    }

    if (hdr.record_count > 0) {
        if (fread(db->records, sizeof(jade_object_t), hdr.record_count, fp) != hdr.record_count) {
            free(db->records);
            db->records = NULL;
            fclose(fp);
            return JADE_ERR_DB_CREATE_FAILED;
        }
    }

    fclose(fp);
    return JADE_OK;
}

static jade_object_t* find_record_by_dnt(jade_db_t* db, uint32_t dnt, uint32_t* out_index) {
    for (uint32_t i = 0; i < db->record_count; i++) {
        if (db->records[i].dnt == dnt) {
            if (out_index) *out_index = i;
            return &db->records[i];
        }
    }
    return NULL;
}

JADE_API int jade_system_init(void) {
    return JADE_OK;
}

JADE_API int jade_system_term(void) {
    return JADE_OK;
}

JADE_API int jade_db_open(const char* base_dir, const char* db_name, bool create_if_missing, jade_db_t** out_db) {
    if (!base_dir || !db_name || !out_db) {
        return JADE_ERR_INIT_FAILED;
    }

    jade_db_t* db = (jade_db_t*)calloc(1, sizeof(jade_db_t));
    if (!db) {
        return JADE_ERR_INIT_FAILED;
    }

    pthread_mutex_init(&db->lock, NULL);
    strncpy(db->base_dir, base_dir, sizeof(db->base_dir) - 1);
    int n = snprintf(db->db_path, sizeof(db->db_path), "%s/%s", base_dir, db_name);
    if (n < 0 || (size_t)n >= sizeof(db->db_path)) {
        pthread_mutex_destroy(&db->lock);
        free(db);
        return JADE_ERR_INIT_FAILED;
    }

    if (mkdir_p(base_dir) != 0) {
        pthread_mutex_destroy(&db->lock);
        free(db);
        return JADE_ERR_INIT_FAILED;
    }

    struct stat st;
    bool exists = (stat(db->db_path, &st) == 0 && S_ISREG(st.st_mode));

    if (!exists) {
        if (!create_if_missing) {
            pthread_mutex_destroy(&db->lock);
            free(db);
            return JADE_ERR_RECORD_NOT_FOUND;
        }

        db->next_dnt = 1;
        db->rid_state.current_rid = 1000;
        db->rid_state.max_rid = 1000000000;
        db->rid_state.block_size = 500;
        db->rid_state.simulate_exhaustion = false;
        db->record_capacity = 64;
        db->record_count = 0;
        db->records = (jade_object_t*)calloc(db->record_capacity, sizeof(jade_object_t));
        if (!db->records) {
            pthread_mutex_destroy(&db->lock);
            free(db);
            return JADE_ERR_INIT_FAILED;
        }

        int rc = flush_db(db);
        if (rc != JADE_OK) {
            free(db->records);
            pthread_mutex_destroy(&db->lock);
            free(db);
            return JADE_ERR_DB_CREATE_FAILED;
        }
    } else {
        int rc = load_db(db);
        if (rc != JADE_OK) {
            pthread_mutex_destroy(&db->lock);
            free(db);
            return rc;
        }
    }

    *out_db = db;
    return JADE_OK;
}

JADE_API int jade_db_close(jade_db_t* db) {
    if (!db) return JADE_OK;
    pthread_mutex_lock(&db->lock);
    flush_db(db);
    free(db->records);
    db->records = NULL;
    pthread_mutex_unlock(&db->lock);
    pthread_mutex_destroy(&db->lock);
    free(db);
    return JADE_OK;
}

JADE_API int jade_add_object(jade_db_t* db, const jade_object_t* obj, uint32_t* out_dnt) {
    if (!db || !obj) return JADE_ERR_INIT_FAILED;

    pthread_mutex_lock(&db->lock);

    if (u16_len(obj->sam_account_name, 256) > 0) {
        for (uint32_t i = 0; i < db->record_count; i++) {
            if (u16_icmp(db->records[i].sam_account_name, obj->sam_account_name, 256) == 0) {
                pthread_mutex_unlock(&db->lock);
                return JADE_ERR_DUPLICATE_SAM;
            }
        }
    }

    if (db->record_count >= db->record_capacity) {
        uint32_t new_cap = (db->record_capacity == 0) ? 64 : db->record_capacity * 2;
        jade_object_t* grown = (jade_object_t*)realloc(db->records, new_cap * sizeof(jade_object_t));
        if (!grown) {
            pthread_mutex_unlock(&db->lock);
            return JADE_ERR_TRANSACTION_FAILED;
        }
        db->records = grown;
        db->record_capacity = new_cap;
    }

    jade_object_t rec;
    memcpy(&rec, obj, sizeof(rec));
    rec.dnt = db->next_dnt++;

    if (is_guid_empty(rec.object_guid)) {
        generate_random_guid(rec.object_guid);
    }

    int64_t now = get_current_filetime();
    if (rec.when_created == 0) rec.when_created = now;
    if (rec.when_changed == 0) rec.when_changed = now;

    db->records[db->record_count++] = rec;

    int rc = flush_db(db);
    if (rc != JADE_OK) {
        db->record_count--;
        db->next_dnt--;
        pthread_mutex_unlock(&db->lock);
        return rc;
    }

    if (out_dnt) *out_dnt = rec.dnt;
    pthread_mutex_unlock(&db->lock);
    return JADE_OK;
}

JADE_API int jade_find_by_sam(jade_db_t* db, const jade_wchar_t* sam, jade_object_t* out_obj) {
    if (!db || !sam || !out_obj) return JADE_ERR_INIT_FAILED;

    pthread_mutex_lock(&db->lock);
    for (uint32_t i = 0; i < db->record_count; i++) {
        if (u16_icmp(db->records[i].sam_account_name, sam, 256) == 0) {
            memcpy(out_obj, &db->records[i], sizeof(jade_object_t));
            pthread_mutex_unlock(&db->lock);
            return JADE_OK;
        }
    }
    pthread_mutex_unlock(&db->lock);
    return JADE_ERR_RECORD_NOT_FOUND;
}

JADE_API int jade_find_by_dnt(jade_db_t* db, uint32_t dnt, jade_object_t* out_obj) {
    if (!db || !out_obj) return JADE_ERR_INIT_FAILED;

    pthread_mutex_lock(&db->lock);
    jade_object_t* rec = find_record_by_dnt(db, dnt, NULL);
    if (!rec) {
        pthread_mutex_unlock(&db->lock);
        return JADE_ERR_RECORD_NOT_FOUND;
    }
    memcpy(out_obj, rec, sizeof(jade_object_t));
    pthread_mutex_unlock(&db->lock);
    return JADE_OK;
}

JADE_API int jade_modify_uac(jade_db_t* db, uint32_t dnt, uint32_t new_uac) {
    if (!db) return JADE_ERR_INIT_FAILED;

    pthread_mutex_lock(&db->lock);
    jade_object_t* rec = find_record_by_dnt(db, dnt, NULL);
    if (!rec) {
        pthread_mutex_unlock(&db->lock);
        return JADE_ERR_RECORD_NOT_FOUND;
    }

    uint32_t prev_uac = rec->user_account_control;
    int64_t prev_changed = rec->when_changed;
    rec->user_account_control = new_uac;
    rec->when_changed = get_current_filetime();

    int rc = flush_db(db);
    if (rc != JADE_OK) {
        rec->user_account_control = prev_uac;
        rec->when_changed = prev_changed;
    }
    pthread_mutex_unlock(&db->lock);
    return rc;
}

JADE_API int jade_modify_ncdnt(jade_db_t* db, uint32_t dnt, uint32_t new_ncdnt) {
    if (!db) return JADE_ERR_INIT_FAILED;

    pthread_mutex_lock(&db->lock);
    jade_object_t* rec = find_record_by_dnt(db, dnt, NULL);
    if (!rec) {
        pthread_mutex_unlock(&db->lock);
        return JADE_ERR_RECORD_NOT_FOUND;
    }

    uint32_t prev_ncdnt = rec->ncdnt;
    int64_t prev_changed = rec->when_changed;
    rec->ncdnt = new_ncdnt;
    rec->when_changed = get_current_filetime();

    int rc = flush_db(db);
    if (rc != JADE_OK) {
        rec->ncdnt = prev_ncdnt;
        rec->when_changed = prev_changed;
    }
    pthread_mutex_unlock(&db->lock);
    return rc;
}

JADE_API int jade_set_password(jade_db_t* db, uint32_t dnt, const uint8_t* pwd_bytes, uint32_t pwd_len) {
    if (!db || !pwd_bytes) return JADE_ERR_INIT_FAILED;
    if (pwd_len > 512) return JADE_ERR_TRANSACTION_FAILED;

    pthread_mutex_lock(&db->lock);
    jade_object_t* rec = find_record_by_dnt(db, dnt, NULL);
    if (!rec) {
        pthread_mutex_unlock(&db->lock);
        return JADE_ERR_RECORD_NOT_FOUND;
    }

    memset(rec->unicode_pwd, 0, sizeof(rec->unicode_pwd));
    if (pwd_len > 0) {
        memcpy(rec->unicode_pwd, pwd_bytes, pwd_len);
    }
    rec->unicode_pwd_len = pwd_len;
    rec->when_changed = get_current_filetime();

    int rc = flush_db(db);
    pthread_mutex_unlock(&db->lock);
    return rc;
}

JADE_API int jade_delete_object(jade_db_t* db, uint32_t dnt, bool tombstone) {
    if (!db) return JADE_ERR_INIT_FAILED;

    pthread_mutex_lock(&db->lock);
    uint32_t idx = 0;
    jade_object_t* rec = find_record_by_dnt(db, dnt, &idx);
    if (!rec) {
        pthread_mutex_unlock(&db->lock);
        return JADE_ERR_RECORD_NOT_FOUND;
    }

    if (tombstone) {
        rec->is_deleted = true;
        char suffix[32];
        int slen = snprintf(suffix, sizeof(suffix), "\nDEL:%u", dnt);
        size_t old_len = u16_len(rec->sam_account_name, 255);
        size_t max_copy = (old_len + (size_t)slen < 255) ? (size_t)slen : (255 - old_len);
        for (size_t i = 0; i < max_copy; i++) {
            rec->sam_account_name[old_len + i] = (uint16_t)(unsigned char)suffix[i];
        }
        rec->sam_account_name[old_len + max_copy] = 0;
        rec->when_changed = get_current_filetime();
    } else {
        if (idx + 1 < db->record_count) {
            memmove(&db->records[idx], &db->records[idx + 1], (db->record_count - idx - 1) * sizeof(jade_object_t));
        }
        db->record_count--;
    }

    int rc = flush_db(db);
    pthread_mutex_unlock(&db->lock);
    return rc;
}

JADE_API int jade_allocate_rid(jade_db_t* db, uint32_t* out_rid) {
    if (!db || !out_rid) return JADE_ERR_INIT_FAILED;

    pthread_mutex_lock(&db->lock);
    if (db->rid_state.simulate_exhaustion || db->rid_state.current_rid >= db->rid_state.max_rid) {
        pthread_mutex_unlock(&db->lock);
        return JADE_ERR_RID_EXHAUSTED;
    }

    uint32_t allocated = db->rid_state.current_rid;
    db->rid_state.current_rid++;

    int rc = flush_db(db);
    if (rc != JADE_OK) {
        db->rid_state.current_rid--;
        pthread_mutex_unlock(&db->lock);
        return rc;
    }

    *out_rid = allocated;
    pthread_mutex_unlock(&db->lock);
    return JADE_OK;
}

JADE_API int jade_set_rid_exhaustion_simulation(jade_db_t* db, bool active) {
    if (!db) return JADE_ERR_INIT_FAILED;

    pthread_mutex_lock(&db->lock);
    db->rid_state.simulate_exhaustion = active;
    int rc = flush_db(db);
    pthread_mutex_unlock(&db->lock);
    return rc;
}

JADE_API int jade_get_rid_state(jade_db_t* db, jade_rid_state_t* out_state) {
    if (!db || !out_state) return JADE_ERR_INIT_FAILED;

    pthread_mutex_lock(&db->lock);
    *out_state = db->rid_state;
    pthread_mutex_unlock(&db->lock);
    return JADE_OK;
}

JADE_API int jade_count_objects(jade_db_t* db, uint32_t* out_count) {
    if (!db || !out_count) return JADE_ERR_INIT_FAILED;

    pthread_mutex_lock(&db->lock);
    *out_count = db->record_count;
    pthread_mutex_unlock(&db->lock);
    return JADE_OK;
}

#endif /* !_WIN32 */
