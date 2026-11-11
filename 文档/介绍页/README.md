# 游戏内模组管理器 · 介绍站（本地）

六个静态页面，直接双击 `index.html` 就能在浏览器里看，不需要联网或服务器。

| 文件 | 内容 |
|---|---|
| `index.html` | 首页：一句话介绍、截图、五个板块入口、生效流程、备份说明 |
| `mods.html` | 模组管理 |
| `config.html` | 模组配置 |
| `profiles.html` | 多配置切换 |
| `packs.html` | 整合包 |
| `language.html` | 多语言翻译 |

## 改内容

正文都写在 `build.py` 的 `PAGES` 里，改完运行：

```
python -I build.py
```

会重新生成六个 `.html`。顶栏、页脚、上下页按钮由脚本统一生成，不要直接改 `.html`。样式在 `site.css`，板块颜色是开头那几个 `--mods` / `--config` / `--profiles` / `--packs` / `--lang`。

## 图片

`assets/` 里：`settings-tree.jpg` 是从 `图片/白天1.png` 压缩的（2560 → 1600 宽，4.2 MB → 131 KB）；`toolbar.png`、`chips.png`、`flow.png`、`boxes.png` 是内置使用说明的图例。想换截图就替换同名文件。

## 以后发布到 GitHub Pages

整个文件夹原样放进仓库的 `docs/`，在仓库 Settings → Pages 选 `docs` 目录即可。所有路径都是相对路径，不用改。
