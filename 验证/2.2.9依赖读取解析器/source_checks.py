# 2.2.9 源码断言：收集器读取元数据时带解析器，调用方传真实游戏根。
import io, os, sys
root = os.path.abspath(os.path.join(os.path.dirname(__file__), '..', '..'))
def read(p): return io.open(os.path.join(root, p), encoding='utf-8').read()
c = read('src/Updating/ComponentDependencyCollector.cs')
checks = [
    ('读取插件程序集带解析器', 'AssemblyResolver = resolver' in c),
    ('解析器清空默认搜索路径', 'RemoveSearchDirectory' in c),
    ('解析器只加 core 与 Managed', 'Path.Combine(loaderRoot, "core"), managedRoot' in c),
    ('Collect 接受 gameRootPath', 'string? gameRootPath = null' in c),
    ('插件侧传 Paths.GameRootPath', 'captured, BepInEx.Paths.GameRootPath)' in read('src/Overview/InfrastructureDependencyCollector.cs')),
    ('worker 侧传 GameRootFor', 'claims, Files.GameRootFor(storage.Root))' in read('Updater/ShopComponentProtection.cs')),
    ('PluginVersion 与 manifest 一致', __import__('re').search(r'PluginVersion = "([^"]+)"', read('src/Plugin.cs')).group(1)
        == __import__('json').loads(read('manifest.json').lstrip('﻿'))['version_number']),
]
bad = [n for n, ok in checks if not ok]
for n, ok in checks: print(('PASS ' if ok else 'FAIL ') + n)
sys.exit(1 if bad else 0)
