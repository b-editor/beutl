﻿//------------------------------------------------------------------------------
// <auto-generated>
//     このコードはツールによって生成されました。
//     ランタイム バージョン:4.0.30319.42000
//
//     このファイルへの変更は、以下の状況下で不正な動作の原因になったり、
//     コードが再生成されるときに損失したりします。
// </auto-generated>
//------------------------------------------------------------------------------

namespace Beutl.Extensions.MediaFoundation.Properties {
    using System;
    
    
    /// <summary>
    ///   ローカライズされた文字列などを検索するための、厳密に型指定されたリソース クラスです。
    /// </summary>
    // このクラスは StronglyTypedResourceBuilder クラスが ResGen
    // または Visual Studio のようなツールを使用して自動生成されました。
    // メンバーを追加または削除するには、.ResX ファイルを編集して、/str オプションと共に
    // ResGen を実行し直すか、または VS プロジェクトをビルドし直します。
    [global::System.CodeDom.Compiler.GeneratedCodeAttribute("System.Resources.Tools.StronglyTypedResourceBuilder", "17.0.0.0")]
    [global::System.Diagnostics.DebuggerNonUserCodeAttribute()]
    [global::System.Runtime.CompilerServices.CompilerGeneratedAttribute()]
    public class Strings {
        
        private static global::System.Resources.ResourceManager resourceMan;
        
        private static global::System.Globalization.CultureInfo resourceCulture;
        
        [global::System.Diagnostics.CodeAnalysis.SuppressMessageAttribute("Microsoft.Performance", "CA1811:AvoidUncalledPrivateCode")]
        internal Strings() {
        }
        
        /// <summary>
        ///   このクラスで使用されているキャッシュされた ResourceManager インスタンスを返します。
        /// </summary>
        [global::System.ComponentModel.EditorBrowsableAttribute(global::System.ComponentModel.EditorBrowsableState.Advanced)]
        public static global::System.Resources.ResourceManager ResourceManager {
            get {
                if (object.ReferenceEquals(resourceMan, null)) {
                    global::System.Resources.ResourceManager temp = new global::System.Resources.ResourceManager("Beutl.Extensions.MediaFoundation.Properties.Strings", typeof(Strings).Assembly);
                    resourceMan = temp;
                }
                return resourceMan;
            }
        }
        
        /// <summary>
        ///   すべてについて、現在のスレッドの CurrentUICulture プロパティをオーバーライドします
        ///   現在のスレッドの CurrentUICulture プロパティをオーバーライドします。
        /// </summary>
        [global::System.ComponentModel.EditorBrowsableAttribute(global::System.ComponentModel.EditorBrowsableState.Advanced)]
        public static global::System.Globalization.CultureInfo Culture {
            get {
                return resourceCulture;
            }
            set {
                resourceCulture = value;
            }
        }
        
        /// <summary>
        ///   Cache に類似しているローカライズされた文字列を検索します。
        /// </summary>
        public static string Cache {
            get {
                return ResourceManager.GetString("Cache", resourceCulture);
            }
        }
        
        /// <summary>
        ///   Media Foundation Decoding に類似しているローカライズされた文字列を検索します。
        /// </summary>
        public static string DecodingName {
            get {
                return ResourceManager.GetString("DecodingName", resourceCulture);
            }
        }
        
        /// <summary>
        ///   Number of samples to cache に類似しているローカライズされた文字列を検索します。
        /// </summary>
        public static string MaxAudioBufferSize {
            get {
                return ResourceManager.GetString("MaxAudioBufferSize", resourceCulture);
            }
        }
        
        /// <summary>
        ///   Number of frames to cache に類似しているローカライズされた文字列を検索します。
        /// </summary>
        public static string MaxVideoBufferSize {
            get {
                return ResourceManager.GetString("MaxVideoBufferSize", resourceCulture);
            }
        }
        
        /// <summary>
        ///   Seek threshold in audio stream に類似しているローカライズされた文字列を検索します。
        /// </summary>
        public static string SeekThresholdInAudioStream {
            get {
                return ResourceManager.GetString("SeekThresholdInAudioStream", resourceCulture);
            }
        }
        
        /// <summary>
        ///   If frames farther than this number of samples are read, seek is performed. に類似しているローカライズされた文字列を検索します。
        /// </summary>
        public static string SeekThresholdInAudioStream_Description {
            get {
                return ResourceManager.GetString("SeekThresholdInAudioStream_Description", resourceCulture);
            }
        }
        
        /// <summary>
        ///   Seek threshold in video stream に類似しているローカライズされた文字列を検索します。
        /// </summary>
        public static string SeekThresholdInVideoStream {
            get {
                return ResourceManager.GetString("SeekThresholdInVideoStream", resourceCulture);
            }
        }
        
        /// <summary>
        ///   If frames farther than this number of frames are read, seek is performed. に類似しているローカライズされた文字列を検索します。
        /// </summary>
        public static string SeekThresholdInVideoStream_Description {
            get {
                return ResourceManager.GetString("SeekThresholdInVideoStream_Description", resourceCulture);
            }
        }
        
        /// <summary>
        ///   Use DXVA2 に類似しているローカライズされた文字列を検索します。
        /// </summary>
        public static string UseDXVA2 {
            get {
                return ResourceManager.GetString("UseDXVA2", resourceCulture);
            }
        }
        
        /// <summary>
        ///   Decode videos at high speed using DirectX Video Accelerator 2.0. に類似しているローカライズされた文字列を検索します。
        /// </summary>
        public static string UseDXVA2_Description {
            get {
                return ResourceManager.GetString("UseDXVA2_Description", resourceCulture);
            }
        }
    }
}
