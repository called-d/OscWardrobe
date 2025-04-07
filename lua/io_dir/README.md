# lua\io_dir について
このフォルダは以下は lua の io 入出力機能で読み書きが可能な場所として用意してあります。
直接 main.lua を書き換えることはできませんが、dofile('io_dir/foo.lua') のように呼ぶ
ことで、このフォルダ内の lua ファイルを実行することもできます。

about lua\io_dir
This folder is prepared as a location where Lua's I/O library can read and write
files. You cannot directly modify `main.lua`, but you can execute Lua files in
this folder by calling something like `dofile('io_dir/foo.lua')`.
