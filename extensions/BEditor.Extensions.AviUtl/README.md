# BEditor.Extensions.AviUtl

## Description

BEditorとAviUtlの相互運用ツール

## Dependent libraries
* [.NET Runtime](https://github.com/dotnet/runtime)
* [Microsoft.Extensions.Configuration](https://github.com/dotnet/runtime)
* [Microsoft.Extensions.Configuration.Binder](https://github.com/dotnet/runtime)

## License

* [MIT License](https://github.com/b-editor/BEditor/blob/main/LICENSE)

## Status
### 変数
* [x] `obj.ox`
* [x] `obj.oy`
* [x] `obj.oz`
* [x] `obj.rx`
* [x] `obj.ry`
* [x] `obj.rz`
* [x] `obj.cx`
* [x] `obj.cy`
* [x] `obj.cz`
* [x] `obj.zoom`
* [x] `obj.alpha`
* [x] `obj.aspect`
* [x] `obj.x`
* [x] `obj.y`
* [x] `obj.z`
* [x] `obj.w`
* [x] `obj.h`
* [x] `obj.screen_w`
* [x] `obj.screen_h`
* [x] `obj.framerate`
* [x] `obj.frame`
* [x] `obj.time`
* [x] `obj.totalframe`
* [x] `obj.totaltime`
* [x] `obj.layer`
* [x] `obj.index`
* [x] `obj.num`
* [x] `obj.track0`
* [x] `obj.track1`
* [x] `obj.track2`
* [x] `obj.track3`
* [x] `obj.check0`
* [x] `color`
* [x] `file`
* [x] `obj.zoom_w`
* [x] `obj.zoom_h`

### 関数
* [x] `obj.mes(text)`
* [ ] `obj.effect([name,param1,value1,param2,value2,...])`
* [x] `obj.draw([ox,oy,oz,zoom,alpha,rx,ry,rz])`
* [x] `obj.drawpoly(x0,y0,z0,x1,y1,z1,x2,y2,z2,x3,y3,z3[,u0,v0,u1,v1,u2,v2,u3,v3,alpha])`
* `obj.load([type],...)`
    * [x] `movie`
    * [x] `image`
    * [x] `text`
    * [x] `figure`
    * [x] `framebuffer`
    * [x] `tempbuffer`
    * [ ] `layer`
    * [ ] `before`
* [x] `obj.setfont(name,size[,type,col1,col2])`
* [x] `obj.rand(st_num,ed_num[,seed,frame])`
* `obj.setoption(name,value)`
    * [ ] `culling`
    * [ ] `billboard`
    * [ ] `shadow`
    * [ ] `antialias`
    * [ ] `blend`
    * [x] `drawtarget`
    * [x] `draw_state`
    * [ ] `focus_mode`
    * [ ] `camera_param`
* `obj.getoption(name,...)`
    * [ ] `track_mode`
    * [x] `script_name`
    * [x] `gui`
    * [x] `camera_mode`
    * [ ] `camera_param`
    * [x] `multi_object`
* `obj.getvalue(target[,time,section])`
    * [x] `0`
    * [x] `1`
    * [x] `2`
    * [x] `3`
    * [x] `x`
    * [x] `y`
    * [x] `z`
    * [x] `rx`
    * [x] `ry`
    * [x] `rz`
    * [x] `zoom`
    * [x] `alpha`
    * [x] `aspect`
    * [x] `time`
    * [x] `layer.x`
    * [ ] `scenechange`
* [ ] `obj.setanchor(name,num[,option,..])`
* [ ] `obj.getaudio(buf,file,type,size)`
* [ ] `obj.filter(name[,param1,value1,param2,value2,...])`
* [x] `obj.copybuffer(dst,src)`
* [x] `obj.getpixel(x,y[,type])`
* [x] `obj.putpixel(x,y,...)`
* [x] `obj.copypixel(dst_x,dst_y,src_x,src_y)`
* `obj.pixeloption(name,value)`
    * [x] `type`
    * [x] `get`
    * [x] `put`
    * [ ] `blend`
* [ ] `obj.getpixeldata([option,...])`
* [ ] `obj.putpixeldata(data)`
* [ ] `obj.getpoint(target[,option])`
* [x] `obj.getinfo(name,...)`
* [x] `obj.interpolation(time,x0,y0,z0,x1,y1,z1,x2,y2,z2,x3,y3,z3)`
* [x] `RGB(r,g,b)`
* [x] `HSV(h,s,v)`
* [ ] `OR(a,b) / AND(a,b) / XOR(a,b)`
* [ ] `SHIFT(a,shift)`
* [ ] `debug_print(text)`