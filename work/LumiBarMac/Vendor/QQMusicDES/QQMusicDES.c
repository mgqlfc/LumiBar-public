#include "QQMusicDES.h"
#include "des.h"

void qq_qrc_decrypt(unsigned char *data, size_t length) {
    static const unsigned char key[] = "!@#)(*$%123ZXC!@!@#)(NHL";
    BYTE schedule[3][16][6];
    three_des_key_setup(key, schedule, DES_DECRYPT);
    for (size_t index = 0; index + DES_BLOCK_SIZE <= length; index += DES_BLOCK_SIZE) {
        three_des_crypt(data + index, data + index, schedule);
    }
}
