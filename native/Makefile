CC = gcc
CFLAGS = -Wall -Wextra -O2 -Iinclude -std=c99
BIN_DIR = bin

ifeq ($(OS),Windows_NT)
    LDFLAGS = -lesent -lole32
    EXE_TARGET = $(BIN_DIR)/jade.exe
    LIB_TARGET = $(BIN_DIR)/jade.dll
    LIB_CFLAGS = -shared -DJADE_EXPORTS
    EXE_RPATH =
else
    UNAME_S := $(shell uname -s)
    LDFLAGS = -lpthread
    EXE_TARGET = $(BIN_DIR)/jade
    LIB_CFLAGS = -fPIC -shared -fvisibility=hidden -DJADE_EXPORTS
    ifeq ($(UNAME_S),Darwin)
        LIB_TARGET = $(BIN_DIR)/libjade.dylib
        EXE_RPATH = -Wl,-rpath,@loader_path
    else
        LIB_TARGET = $(BIN_DIR)/libjade.so
        EXE_RPATH = -Wl,-rpath,'$$ORIGIN'
    endif
endif

all: $(BIN_DIR) $(LIB_TARGET) $(EXE_TARGET)

$(BIN_DIR):
	mkdir -p $(BIN_DIR)

$(LIB_TARGET): src/jade_core.c src/jade_core_posix.c include/jade.h
	$(CC) $(LIB_CFLAGS) src/jade_core.c $(CFLAGS) $(LDFLAGS) -o $(LIB_TARGET)

$(EXE_TARGET): src/main.c $(LIB_TARGET)
	$(CC) src/main.c $(CFLAGS) -L$(BIN_DIR) -ljade $(EXE_RPATH) $(LDFLAGS) -o $(EXE_TARGET)

clean:
	rm -rf $(BIN_DIR)/*.exe $(BIN_DIR)/*.dll $(BIN_DIR)/*.so $(BIN_DIR)/*.dylib $(BIN_DIR)/jade
