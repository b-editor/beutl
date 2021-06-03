# BEditor.Extension.AviUtl

## Description

BEditorとAviUtlの相互運用ツール

## Dependent libraries
* [.NET Runtime](https://github.com/dotnet/runtime)
* [Microsoft.Extensions.Configuration](https://github.com/dotnet/runtime)
* [Microsoft.Extensions.Configuration.Binder](https://github.com/dotnet/runtime)

## License

* [MIT License](https://github.com/b-editor/BEditor/blob/main/LICENSE)

## 実装関数
* [x] `obj.mes(text)`
* [ ] `obj.effect([name,param1,value1,param2,value2,...])`
* [x] `obj.draw([ox,oy,oz,zoom,alpha,rx,ry,rz])`
* [x] `obj.drawpoly(x0,y0,z0,x1,y1,z1,x2,y2,z2,x3,y3,z3[,u0,v0,u1,v1,u2,v2,u3,v3,alpha])`
* [x] `obj.load([type],...)`
* [x] `obj.setfont(name,size[,type,col1,col2])`
* [x] `obj.rand(st_num,ed_num[,seed,frame])`
* [x] `obj.setoption(name,value)`
* [ ] `obj.getoption(name,...)`
* [ ] `obj.getvalue(target[,time,section])`
* [ ] `obj.setanchor(name,num[,option,..])`
* [ ] `obj.getaudio(buf,file,type,size)`
* [ ] `obj.filter(name[,param1,value1,param2,value2,...])`
* [ ] `obj.copybuffer(dst,src)`
* [ ] `obj.getpixel(x,y[,type])`
* [ ] `obj.putpixel(x,y,...)`
* [ ] `obj.copypixel(dst_x,dst_y,src_x,src_y)`
* [ ] `obj.pixeloption(name,value)`
* [ ] `obj.getpixeldata([option,...])`
* [ ] `obj.putpixeldata(data)`
* [ ] `obj.getpoint(target[,option])`
* [ ] `obj.getinfo(name,...)`
* [x] `obj.interpolation(time,x0,y0,z0,x1,y1,z1,x2,y2,z2,x3,y3,z3)`
* [ ] `RGB(r,g,b)`
* [ ] `HSV(h,s,v)`
* [ ] `OR(a,b) / AND(a,b) / XOR(a,b)`
* [ ] `SHIFT(a,shift)`
* [ ] `debug_print(text)`