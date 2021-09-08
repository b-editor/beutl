## インストールまたは実行
### Ubuntu.18.04
#### Zipファイルから使う場合
実行時に以下のコマンドを実行してください。  
実行しない場合OpenCVに依存しているエフェクトが使えない場合があります。  
``` bash
sudo apt install libopenexr22 \
    libharfbuzz0b \
    libhdf5-100 \
    libwebp6 \
    libjasper1 \
    libilmbase12 \
    libgstreamer1.0-0 \
    libavcodec-extra57 \
    libavformat57 \
    libswscale4 \
    libgtk-3-0 \
    libtesseract4 \
    libgtk2.0-0 \
    libdc1394-22
```

インストールされてない場合、OpenALをインストール
``` bash
sudo apt install libopenal1
```

**beditor_linux-x64.zip** をダウンロードし展開  
**beditor** ファイルのあるディレクトリで以下のコマンドを実行します。  
``` bash
./beditor
```

#### Debパッケージから使う場合
**beditor_x.x.x-1_amd64.deb** をダウンロードします。  
以下のコマンドを実行します。  
``` bash
sudo apt install beditor_x.x.x-1_amd64.deb
```

### Windows
#### Zipファイルから使う場合

~~[OpenAL](https://www.openal.org/) をインストールします。~~  
**beditor_win-x64.zip** をダウンロードし展開  
**beditor_win-x64** 内の **beditor.exe** を実行します。

#### インストーラーからインストール

