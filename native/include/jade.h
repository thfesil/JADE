#ifndef JADE_H
#define JADE_H

#include <windows.h>
#include <esent.h>
#include <stdint.h>
#include <stdbool.h>

#ifdef JADE_EXPORTS
    #define JADE_API __declspec(dllexport)
#else
    #define JADE_API __declspec(dllimport)
#endif

/* Fallback definitions for standard ESENT constants if omitted by MinGW headers */
#ifndef JET_prepInsert
#define JET_prepInsert              0
#endif
#ifndef JET_prepReplace
#define JET_prepReplace             2
#endif
#ifndef JET_bitNewKey
#define JET_bitNewKey               0x00000001
#endif
#ifndef JET_bitSeekEQ
#define JET_bitSeekEQ               0x00000001
#endif
#ifndef JET_bitTermComplete
#define JET_bitTermComplete         0x00000001
#endif
#ifndef JET_bitColumnAutoincrement
#define JET_bitColumnAutoincrement  0x00000010
#endif
#ifndef JET_bitIndexUnique
#define JET_bitIndexUnique          0x00000001
#endif
#ifndef JET_bitIndexPrimary
#define JET_bitIndexPrimary         0x00000002
#endif
#ifndef JET_bitIndexDisallowNull
#define JET_bitIndexDisallowNull    0x00000004
#endif
#ifndef JET_bitIndexIgnoreNull
#define JET_bitIndexIgnoreNull      0x00000008
#endif

#ifdef __cplusplus
extern "C" {
#endif

/* Active Directory Object Classes */
#define JADE_CLASS_CONTAINER           196613
#define JADE_CLASS_ORGANIZATIONAL_UNIT 196614
#define JADE_CLASS_USER                196621
#define JADE_CLASS_GROUP               196622

/* Active Directory UserAccountControl Flags */
#define JADE_UAC_ACCOUNTDISABLE        0x0002 /* 514 when active + disable */
#define JADE_UAC_NORMAL_ACCOUNT        0x0200 /* 512 */
#define JADE_UAC_DONT_EXPIRE_PASSWORD  0x10000 /* 66048 */

/* Custom Jade and Windows Error Codes */
#define JADE_OK                        0
#define JADE_ERR_INIT_FAILED          -1
#define JADE_ERR_DB_CREATE_FAILED     -2
#define JADE_ERR_TABLE_CREATE_FAILED  -3
#define JADE_ERR_RECORD_NOT_FOUND     -4
#define JADE_ERR_DUPLICATE_SAM        -5
#define JADE_ERR_RID_EXHAUSTED        8209 /* Win32 ERROR_DS_RIDM_UNWILLING_TO_PERFORM / 00002011 */
#define JADE_ERR_TRANSACTION_FAILED   -7

/* Structure representing an object in the simulated NTDS.DIT datatable */
typedef struct {
    uint32_t dnt;
    uint32_t pdnt;
    uint32_t ncdnt;
    uint32_t object_class;
    wchar_t  sam_account_name[256];
    wchar_t  user_principal_name[256];
    uint32_t user_account_control;
    uint8_t  unicode_pwd[512];
    uint32_t unicode_pwd_len;
    uint8_t  object_guid[16];
    uint8_t  object_sid[68];
    uint32_t object_sid_len;
    bool     is_deleted;
    int64_t  when_created;
    int64_t  when_changed;
} jade_object_t;

/* Structure representing the FSMO RID Master state */
typedef struct {
    uint32_t current_rid;
    uint32_t max_rid;
    uint32_t block_size;
    bool     simulate_exhaustion;
} jade_rid_state_t;

/* Opaque database context handle */
typedef struct jade_db_context jade_db_t;

/* Public API Functions */
JADE_API int jade_system_init(void);
JADE_API int jade_system_term(void);

JADE_API int jade_db_open(const char* base_dir, const char* db_name, bool create_if_missing, jade_db_t** out_db);
JADE_API int jade_db_close(jade_db_t* db);

JADE_API int jade_add_object(jade_db_t* db, const jade_object_t* obj, uint32_t* out_dnt);
JADE_API int jade_find_by_sam(jade_db_t* db, const wchar_t* sam, jade_object_t* out_obj);
JADE_API int jade_find_by_dnt(jade_db_t* db, uint32_t dnt, jade_object_t* out_obj);
JADE_API int jade_modify_uac(jade_db_t* db, uint32_t dnt, uint32_t new_uac);
JADE_API int jade_modify_ncdnt(jade_db_t* db, uint32_t dnt, uint32_t new_ncdnt);
JADE_API int jade_set_password(jade_db_t* db, uint32_t dnt, const uint8_t* pwd_bytes, uint32_t pwd_len);
JADE_API int jade_delete_object(jade_db_t* db, uint32_t dnt, bool tombstone);

JADE_API int jade_allocate_rid(jade_db_t* db, uint32_t* out_rid);
JADE_API int jade_set_rid_exhaustion_simulation(jade_db_t* db, bool active);
JADE_API int jade_get_rid_state(jade_db_t* db, jade_rid_state_t* out_state);

JADE_API int jade_count_objects(jade_db_t* db, uint32_t* out_count);

#ifdef __cplusplus
}
#endif

#endif /* JADE_H */
