# 从源码构建

需要已安装的 Steam tModLoader 1.4.4 stable。本次验证使用 2026.8.3.0。

最方便的方法：在 tModLoader 的 Workshop → Develop Mods 中打开 ModSources 文件夹，把仓库中的整个 `MilkcraftTerraria` 文件夹复制进去，选择 Build。产物为 `MilkcraftTerraria.tmod`。

也可以在 PowerShell 使用仓库脚本：

```powershell
.	oolsuild.ps1 -TModLoaderPath '你的 tModLoader 安装目录'
```

脚本使用已安装 tModLoader 自带的 .NET 和编译器，构建数据写入本仓库 `build-sandbox`，产物复制到 `dist`。不会自动运行游戏、启用模组或赠送试玩物品。

制作公开分发包可加 `-PublicPackage`，需要Python 3。此步骤移除调试符号与构建路径，不更改游戏程序集和素材。

```powershell
.\tools\build.ps1 -TModLoaderPath '你的 tModLoader 安装目录' -PublicPackage
```

制作公开分发包可加 `-PublicPackage`，需要Python 3。此步骤移除调试符号与构建路径，不更改游戏程序集和素材。

```powershell
.\tools\build.ps1 -TModLoaderPath '你的 tModLoader 安装目录' -PublicPackage
```
