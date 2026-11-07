CC = gcc
CFLAGS = -Wall -Wextra -O2 -Iinclude
LDFLAGS = -lesent -lole32

BIN_DIR = bin
EXE_TARGET = $(BIN_DIR)/jade.exe
DLL_TARGET = $(BIN_DIR)/jade.dll

all: $(BIN_DIR) $(DLL_TARGET) $(EXE_TARGET)

$(BIN_DIR):
	mkdir -p $(BIN_DIR)

$(DLL_TARGET): src/jade_core.c include/jade.h
	$(CC) -shared -DJADE_EXPORTS src/jade_core.c $(CFLAGS) $(LDFLAGS) -o $(DLL_TARGET)

$(EXE_TARGET): src/main.c $(DLL_TARGET)
	$(CC) src/main.c $(CFLAGS) -L$(BIN_DIR) -ljade $(LDFLAGS) -o $(EXE_TARGET)

clean:
	rm -rf $(BIN_DIR)/*.exe $(BIN_DIR)/*.dll
