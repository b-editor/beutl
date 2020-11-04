using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

namespace BEditor.Graphics {
    public unsafe static partial class GL {
        #region G

        public static void GenTextures(int n, [Out] int[] textures) {
            fixed(int* texs = textures) {
                GenTextures(n, texs);
            }
        }
        public static void GenTextures(int n, out int textures) {
            fixed(int* texs = &textures) {
                GenTextures(n, texs);
            }
        }
        public static void GenTextures(int n, [Out] uint[] textures) {
            fixed(uint* txs = textures) {
                GenTextures(n, txs);
            }
        }
        public static void GenTextures(int n, out uint textures) {
            fixed(uint* texs = &textures) {
                GenTextures(n, texs);
            }
        }

        #endregion
    }
}
