# -*- coding: utf-8 -*-
"""生成游戏内模组管理器介绍站（6 个静态页）。共用顶栏、页脚和上下页；每页正文写在下面的 PAGES 里。
用法：python -I build.py   （在本文件所在目录生成 *.html）"""
import io, os

HERE = os.path.dirname(os.path.abspath(__file__))
NAME = "游戏内模组管理器"

# (文件, 导航名, 板块色)
NAV = [
    ("index.html", "首页", "--pin"),
    ("mods.html", "模组管理", "--mods"),
    ("config.html", "模组配置", "--config"),
    ("profiles.html", "多配置切换", "--profiles"),
    ("packs.html", "整合包", "--packs"),
    ("language.html", "多语言翻译", "--lang"),
]

RIDGE = ('<svg class="ridge" viewBox="0 0 1200 34" preserveAspectRatio="none" aria-hidden="true">'
         '<path d="M0 34 L0 20 L110 12 L200 22 L320 4 L420 18 L540 8 L640 24 L760 2 L880 20 L990 10 L1090 22 L1200 14 L1200 34 Z" fill="#1b3d5c"/>'
         '<path d="M0 34 L0 28 L150 24 L300 30 L460 22 L620 30 L780 24 L940 30 L1100 25 L1200 28 L1200 34 Z" fill="#e6ecef"/></svg>')


def shell(file, title, body):
    links = "".join(
        f'<a href="{f}"' + (f' aria-current="page" style="--c:var({c})"' if f == file else "") + f">{n}</a>"
        for f, n, c in NAV)
    i = [f for f, _, _ in NAV].index(file)
    pager = ""
    if i > 0:
        f, n, c = NAV[i - 1]
        pager += f'<a href="{f}" style="--c:var({c})"><small>上一页</small>{n}</a>'
    else:
        pager += "<span></span>"
    if i < len(NAV) - 1:
        f, n, c = NAV[i + 1]
        pager += f'<a class="next" href="{f}" style="--c:var({c})"><small>下一页</small>{n}</a>'
    return f"""<!doctype html>
<html lang="zh-CN">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width, initial-scale=1">
<title>{title} · {NAME}</title>
<link rel="icon" href="assets/icon.png">
<link rel="stylesheet" href="site.css">
</head>
<body>
<header class="bar"><div class="bar-in">
  <a class="brand" href="index.html"><img src="assets/icon.png" alt=""><span>{NAME}</span></a>
  <nav class="nav" aria-label="板块">{links}</nav>
</div></header>
{RIDGE}
<main>
{body}
<nav class="pager" aria-label="翻页">{pager}</nav>
<footer><span>{NAME} · In-Game Mod Manager · PEAK 模组</span><span>本介绍依据当前开发版整理，界面以游戏内为准</span></footer>
</main>
</body>
</html>
"""


def head(tag, color, title, lede):
    return (f'<header class="page-head" style="--c:var({color})"><div>'
            f'<span class="tag">{tag}</span><h1>{title}</h1><p class="lede">{lede}</p></div></header>')


def points(color, items):
    return f'<ul class="points" style="--c:var({color})">' + "".join(f"<li><b>{t}</b><span>{d}</span></li>" for t, d in items) + "</ul>"


def figure(src, alt, caption, maxw=None):
    style = f' style="max-width:{maxw}px"' if maxw else ""
    return f'<figure class="figure"><img src="assets/{src}" alt="{alt}" loading="lazy"{style}><figcaption>{caption}</figcaption></figure>'


PAGES = {}

# —— 首页 ——
PAGES["index.html"] = ("在游戏里管理所有模组", """
<style>
  .hero { display: grid; grid-template-columns: minmax(0, 1.05fr) minmax(0, 1fr); gap: 34px; align-items: center; padding-top: 10px; }
  .hero h1 { margin-top: 8px; }
  .hero .shot { background: var(--navy); border-radius: 16px; padding: 12px; box-shadow: 0 24px 50px -30px #0c2033; }
  .hero .shot img { border-radius: 9px; }
  .install { display: flex; flex-wrap: wrap; gap: 8px 20px; font-size: 15px; color: var(--ink-2); margin-top: 6px; }
  .install b { color: var(--ink); }
  .blocks { display: grid; grid-template-columns: repeat(5, minmax(0, 1fr)); gap: 12px; margin-top: 14px; }
  .block { display: flex; flex-direction: column; background: var(--paper); border-radius: 14px; padding: 18px 16px 16px; text-decoration: none; color: var(--ink); border-top: 5px solid var(--c); min-height: 260px; }
  .block:hover { background: #fff; }
  .block h3 { color: var(--c); font-size: 19px; margin-bottom: 8px; }
  .block p { font-size: 14px; color: var(--ink-2); margin: 0 0 10px; }
  .block ul { margin: auto 0 0; padding-left: 1.05em; font-size: 14px; }
  .block li { margin: 2px 0; }
  .flowline { display: grid; grid-template-columns: repeat(4, minmax(0, 1fr)); gap: 0; margin-top: 16px; background: var(--paper); border-radius: 14px; overflow: hidden; }
  .flowline div { padding: 16px 18px; border-right: 1px solid var(--line); }
  .flowline div:last-child { border-right: none; }
  .flowline b { display: block; font-size: 16px; }
  .flowline span { font-size: 14px; color: var(--ink-2); }
  .flowline i { display: block; font-style: normal; font-size: 13px; color: var(--ink-2); margin-bottom: 4px; }
  @media (max-width: 980px) { .blocks { grid-template-columns: repeat(2, minmax(0, 1fr)); } .hero { grid-template-columns: 1fr; } }
  @media (max-width: 620px) { .blocks, .flowline { grid-template-columns: 1fr; } .flowline div { border-right: none; border-bottom: 1px solid var(--line); } }
</style>
<section class="hero" style="padding-top:8px">
  <div>
    <h1>不用退出游戏，在设置菜单里管好所有模组</h1>
    <p class="lede">不用退出游戏、不用开管理器，在 PEAK 的设置菜单里就能看到装了哪些模组、各自是什么版本、谁依赖谁，改配置、改快捷键、更新、启停、卸载，都在同一个界面里完成。</p>
    <div class="install"><span><b>安装：</b>用 r2modman 或雷霆商店管理器一键安装，依赖会自动装好</span><span><b>入口：</b>主菜单或暂停菜单的「设置」→「模组概览」</span></div>
  </div>
  <div class="shot"><img src="assets/overview-blue.jpg" alt="模组概览：按分组列出所有模组，每行有启用方框、序号、名称、来源标签、版本、作者和简介"></div>
</section>

<section>
  <h2>五个板块</h2>
  <p class="muted">每块点进去有详细介绍。</p>
  <div class="blocks">
    <a class="block" href="mods.html" style="--c:var(--mods)"><h3>模组管理</h3><p>一页看全所有模组，哪个有更新、哪个没生效、谁依赖谁，一眼就知道。</p><ul><li>按分组排列</li><li>状态角标</li><li>一键启停</li><li>更新、安装、卸载</li><li>依赖与主页</li><li>目录整理</li></ul></a>
    <a class="block" href="config.html" style="--c:var(--config)"><h3>模组配置</h3><p>所有模组的设置集中在一棵树里，找得到、改得明白。</p><ul><li>树形侧栏</li><li>说明面板</li><li>快捷键集中管理</li><li>先改后存</li></ul></a>
    <a class="block" href="profiles.html" style="--c:var(--profiles)"><h3>多配置切换</h3><p>同一套模组可以有好几套配置，比如「日常」和「整活」，随时切换。</p><ul><li>配置方案</li><li>切换只是查看</li><li>作者预设与个人修改分开</li></ul></a>
    <a class="block" href="packs.html" style="--c:var(--packs)"><h3>整合包</h3><p>把一整套模组环境存起来，换着玩，也能打包发给朋友。</p><ul><li>整合包库</li><li>导入先预览</li><li>切换有备份</li><li>写介绍</li><li>发布</li></ul></a>
    <a class="block" href="language.html" style="--c:var(--lang)"><h3>多语言翻译</h3><p>界面和模组的名字、说明都能翻译，补翻也很方便。</p><ul><li>内置三种语言</li><li>模组名称和说明也能翻</li><li>一键导出待翻文本</li><li>跟着整合包走</li><li>不需要 XUnity</li></ul></a>
  </div>
</section>

<section>
  <h2>一次修改是怎么生效的</h2>
  <p class="muted">改参数、改快捷键、勾选启停都是同一个流程。安装、更新、卸载不走这条路，点了就排队执行。</p>
  <div class="flowline">
    <div><i>第一步</i><b>改动</b><span>改一个设置、录一个按键、勾一个模组。</span></div>
    <div><i>第二步</i><b>进草稿</b><span>名称旁出现橙色角标，还没写进文件。</span></div>
    <div><i>第三步</i><b>保存配置</b><span>保存前可以在「变更检查目录」核对每一项。</span></div>
    <div><i>第四步</i><b>按时机生效</b><span>立即、下一局，或者重启游戏。每项都会标出来。</span></div>
  </div>
</section>

<section>
  <h2>安装</h2>
  <div class="two">
    <div><p>用 r2modman 或雷霆商店管理器一键安装，依赖会自动装好。进游戏后，在主菜单或暂停菜单的「设置」里打开「模组概览」。</p></div>
    <div class="callout"><b>不推荐手动安装：</b>依赖比较多，有的还要放进 BepInEx 的 core 文件夹。请用模组管理器安装；自动部署脚本以后会放在这里。</div>
  </div>
</section>

<section>
  <h2>文件放在哪</h2>
  <p class="muted">管理器替你保存的东西都在 <code>BepInEx/config/ModConfigEnhance/</code>，只有四个文件夹。</p>
  <div class="flowline">
    <div><i>language</i><b>语言文件</b><span>你自己的翻译，一种语言一个文件。</span></div>
    <div><i>profiles</i><b>配置方案</b><span>每套方案的参数、快捷键、启停，整合包介绍和素材。</span></div>
    <div><i>output</i><b>导出的东西</b><span>待翻文本、发布的整合包、日志导出。</span></div>
    <div><i>data</i><b>管理器自己的数据</b><span>运行状态、商店缓存、自动备份、你改过的说明。</span></div>
  </div>
  <p class="muted" style="margin-top:12px">已装的整合包和更新助手的备份在 <code>BepInEx/MCE/</code>。</p>
</section>

<section>
  <h2>改文件之前一定先备份</h2>
  <div class="two">
    <div><p>安装、更新、卸载、整理文件夹、切换整合包，这些要动模组文件的操作都由一个独立的小助手执行。它只在有任务时启动，带一个前台进度窗口，做完就退出。</p>
    <p>要替换游戏正在用的文件时，它会等你退出游戏再动手。每次改动前都先备份，失败自动回退；已完成的更新也能手动回退。</p></div>
    <div class="callout"><b>不会被拦下的操作：</b>大部分检查都只是提醒，确认一次就能继续。真正不能做的只有几件：停用或卸载管理器自己和它运行所需的三个库，以及会伤到文件的操作（路径越界、链接、目标被占用）。</div>
  </div>
</section>
""")

# —— 模组管理 ——
PAGES["mods.html"] = ("模组管理", head("模组管理", "--mods", "一页看全所有模组",
    "哪个有更新、哪个没生效、谁依赖谁，打开模组概览一眼就知道。启停、更新、卸载也在这一页做。") + """
<section style="padding-top:28px">
""" + figure("overview-blue.jpg", "模组概览截图：顶部工具条，下面按分组列出模组；鼠标停在名称上显示依赖卡", "模组概览。鼠标停在名称上会弹出依赖卡，列出它依赖的库和谁依赖它。") + """
  <h2 style="margin-top:34px">看懂一行</h2>
  <p>从左到右：启用方框、序号、名称（右上角是状态角标）、齿轮（打开设置）、主页（项目页和商店页）、文件夹（打开所在位置）、版本、作者、简介。鼠标停在名称上会显示依赖卡：它需要哪些模组、哪些模组需要它。</p>
""" + figure("chips.png", "七种名称列角标：未读变动、有新版本、未保存、待重启、嵌套模组、资源包、散放 DLL", "七种角标各一个颜色，位置固定，同一种角标在每一行都在同一个地方。", 900) + """
</section>
<section>
  <h2>主要功能</h2>
""" + points("--mods", [
    ("分组", "分组可以嵌套三层，拖动标题就能挂到别的分组下。删除分组不删文件，组里的模组上移一层。"),
    ("首次自动分组", "第一次打开时，按你 plugins 里现有的分类文件夹生成分组建议，确认才保存。"),
    ("勾选启停", "勾选或取消就行，不用手动改文件名。改动先进草稿，保存后下次启动生效。"),
    ("同一个模组装了两份", "每份单独一行，勾选其中一份会自动停用其他副本。"),
    ("检查更新", "在主菜单和机场自动检查，只检查不下载。有新版时名称和版本号旁出现绿色角标。"),
    ("更新、安装、卸载", "点了就进更新队列，不用保存配置。缺的依赖会一起装上。"),
    ("等退出再替换", "只有要替换正在运行的文件时才等你退出游戏；新装的模组游戏运行时就能装好。"),
    ("回退", "已完成的更新可以回退到更新前的文件，默认保留你现在的参数。"),
    ("搜索与筛选", "按名称、作者、版本、简介搜索；按可更新、重复、异常、有变更等状态筛选。"),
    ("基础组件", "BepInEx 的 core、patchers 里的共用运行库也列出来，可以改译名、启停、卸载。"),
    ("资源包和特殊情况", "只有 manifest 没有 DLL 的资源包、嵌套在别的模组文件夹里的模组、散放的 DLL，都会单独标出。"),
    ("目录整理", "按分组把 plugins 文件夹整理好。只移动不改名，先看预览树，空文件夹可以一起放进回收站。"),
]) + """
</section>
<section>
  <h2>来源：这个模组在商店里是哪个包</h2>
  <div class="two">
    <div><p>确认了来源，才能检查更新、在发布整合包时写对依赖。自动识别用模组的原名去商店找，名称唯一、作者一致就直接匹配；有疑问的交给你手动选。</p>
    <p>通过管理器商店装的模组直接按安装记录确认，不用再识别。确实没有商店版本的，标成「本地」，以后不再查。</p></div>
    <div class="callout"><b>来源只影响更新检查。</b>还没确认来源的模组照样能用、能启停，依赖检查也照常：装了、启用了、版本够就不会报错。</div>
  </div>
</section>
""")

# —— 模组配置 ——
PAGES["config.html"] = ("模组配置", head("模组配置", "--config", "所有模组的设置，集中在一棵树里",
    "在 ModConfig 设置页的基础上加了树形侧栏、说明面板和快捷键页。改动先放进草稿，核对清楚再保存。") + """
<section style="padding-top:28px">
""" + figure("settings-tree.jpg", "模组设置页截图：左侧树形侧栏和工具行，右侧顶部是说明面板，下面是设置项", "左侧按模组和分区排列，可以搜索、置顶；右侧顶部显示当前设置项的说明。") + """
</section>
<section>
  <h2>快捷键页</h2>
""" + figure("hotkeys.jpg", "快捷键页截图：按分组和模组列出每个快捷键，右侧三列是当前按键、模组默认和方案预设，重复的键标着 Duplicate", "每个键三列：当前按键（点击录制）、模组默认、方案预设。用了同一个键的会标「重复」。") + """
</section>
<section>
  <h2>主要功能</h2>
""" + points("--config", [
    ("树形侧栏", "模组和它的分区排成一棵树，跟概览用同一套分组。可以搜索，常用的模组可以置顶。"),
    ("说明面板", "选中或悬停一个设置项，上方显示它的说明、默认值和可选范围。"),
    ("小数正常显示", "模组参数里的小数照常显示和输入，不会被取整。"),
    ("保存和默认并排", "每个设置项右边是保存和恢复默认两个按钮。"),
    ("快捷键页", "所有模组的快捷键放在同一页，每个键三列：当前按键、模组默认、方案目标。点一下直接录新按键。"),
    ("重复键提示", "两个模组用了同一个键会标出来，可以跳到对方那一项，也可以在本机忽略这对冲突。"),
    ("先改草稿", "参数、快捷键、启停改了都先进草稿，名称旁出现橙色角标。游戏崩溃了草稿也还在。"),
    ("变更检查目录", "保存前按模组列出原值、新值和什么时候生效：立即、下一局、重启游戏。"),
    ("离线修改", "模组这次没加载，它已有的配置文件也能改，启用后使用。"),
    ("和别的工具合并", "手动改了 cfg 文件，没冲突的字段自动合并；同一个字段两边都改了，让你选保留哪个。"),
]) + """
</section>
<section>
  <h2>两套配色</h2>
""" + figure("overview-purple.jpg", "紫色主题下的模组概览", "紫色主题。和上面的蓝色是同一个界面。") + """
  <div class="two" style="margin-top:18px">
    <div><p>全部界面有蓝色和紫色两套配色。蓝色跟着游戏 3.1 的设置菜单，紫色是夜间风格。点概览工具条或设置页「搜索」旁的月亮图标切换，也可以选「夜里自动紫色」：装了 TimeTheme 时，游戏里天黑就自动变紫。</p></div>
    <div class="callout">按钮按功能分颜色：<b>绿色</b>提交，<b>红色</b>危险操作，<b>蓝色</b>编辑和选择，<b>青绿</b>打开别的窗口，<b>灰色</b>普通操作，<b>使用说明</b>单独一种颜色。</div>
  </div>
</section>
""")

# —— 多配置切换 ——
PAGES["profiles.html"] = ("多配置切换", head("多配置切换", "--profiles", "同一套模组，好几套配置",
    "比如「日常」和「整活」各一套，参数、快捷键和哪些模组开着各不相同，随时切换。") + """
<section style="padding-top:28px">
  <h2>配置方案里存了什么</h2>
  <div class="two">
    <div><p>一套<b>配置方案</b>只存三样东西：所有模组的参数、快捷键、每个模组开还是关。</p>
    <p>分组、介绍、模组译名这些属于整合包，同一个整合包里的所有方案共用，切换方案时不会变。</p></div>
""" + figure("toolbar.png", "概览顶部工具条：方案下拉、保存配置、变更检查目录等按钮", "工具条最左边的「方案：xxx」就是切换入口。", 720) + """
  </div>
</section>
<section>
  <h2>主要功能</h2>
""" + points("--profiles", [
    ("切换只是查看", "在方案下拉里点一个就切过去看，不会改动正在用的配置。确定要用，再点保存配置。"),
    ("管理方案", "新建、复制、改名、删除，或者把现在实际的配置记成一套新方案。"),
    ("作者预设保持原样", "整合包作者给的方案是只读的。你第一次改它时，可以原地保存或另存一份个人方案。"),
    ("随时恢复作者设置", "改乱了可以把作者原版预设恢复到草稿，方案名后的 * 表示和原版不同。"),
    ("导出配置", "导出成一个 .mceconfig.json 文件，只含参数、快捷键和启停，适合分享一套设置。"),
    ("导入配置", "导入的内容先进草稿，核对后再保存。"),
    ("离开前会问", "有没保存的改动时离开页面或切换方案，会问你保存、放弃还是继续编辑。"),
    ("保存前的检查", "缺依赖、重复模组、来源未识别、冲突建议都会列出来。大多只是提醒，确认就能保存。"),
]) + """
</section>
<section>
  <h2>保存和生效是两回事</h2>
""" + figure("flow.png", "一次修改的流程：改动、草稿、保存配置、生效时机", "启停一律在下次启动生效；参数按模组自己的支持情况，可能立即、下一局或重启后生效。", 900) + """
</section>
""")

# —— 整合包 ——
PAGES["packs.html"] = ("整合包", head("整合包", "--packs", "整套模组环境存起来，换着玩",
    "整合包是一整套模组环境：模组文件、配置、方案、分组和介绍。可以存好几套随时切换，也能打包发给朋友。") + """
<section style="padding-top:28px">
  <h2>玩家这边</h2>
""" + points("--packs", [
    ("整合包库", "可以存好几套完整的模组环境，每套保留自己的模组版本和配置。"),
    ("导入先预览", "导入时先看名称、版本、检查结果、依赖和全部文件。存进库里不会动你现在的游戏文件。"),
    ("缺的依赖", "商店格式的包要下载依赖时，先问你同意再联网。"),
    ("切换有备份", "切换前可以先把当前环境存成新版本。替换文件等你退出游戏后自动完成，提前开游戏会中止并恢复原样。"),
    ("临时整合包", "还没用过整合包时，先把当前环境存成一个临时整合包，不用退出游戏，以后随时切回来。"),
    ("首次进入", "装好作者的整合包，第一次进主菜单时自动应用作者指定的默认方案。"),
    ("个人修改另存", "你改的内容存进个人副本，作者原件保持只读，升级整合包时你的修改还在。"),
    ("空间管理", "整合包库、下载缓存、备份各占多少空间，能看能清。"),
]) + """
</section>
<section>
  <h2>作者这边</h2>
  <div class="two">
    <div>
""" + points("--packs", [
    ("写介绍", "给整合包写图文介绍：标题、颜色、字号、对齐，插图片、动图和视频。玩家打开概览首先看到它。"),
    ("冲突建议", "标出哪些模组不建议同时开，玩家那边会在名称旁看到提示。"),
    ("签名", "给作者资料签名，文件被改过时玩家那边能发现，玩家自己的配置不受影响。"),
]) + """
    </div>
    <div>
      <h3>三种导出方式</h3>
      <ul class="steps" style="--c:var(--packs)">
        <li><span><b>仅作者资料</b><br><span class="muted">方案、介绍、译文和来源。给已经装好模组的玩家。</span></span></li>
        <li><span><b>完整本地整合包</b><br><span class="muted">带上所有模组文件，离线可用。</span></span></li>
        <li><span><b>雷霆商店包</b><br><span class="muted">商店里有的模组自动写成依赖，其余随包附带。生成 ZIP 后由你上传。</span></span></li>
      </ul>
    </div>
  </div>
  <div class="callout">导出配置时只按默认方案里记录的参数生成，<b>不会把你整个 config 文件夹复制出去</b>；要附带别的配置文件，得在发布向导里手动勾选并先预览。缓存、日志、备份和个人运行状态会自动排除。</div>
</section>
""")

# —— 多语言翻译 ——
PAGES["language.html"] = ("多语言翻译", head("多语言翻译", "--lang", "界面和模组说明，都能翻译成你的语言",
    "管理器自己内置英文、简体、繁体三种语言。模组的名字、简介和设置项说明走同一套词典，补翻也方便。") + """
<section style="padding-top:28px">
  <h2>主要功能</h2>
""" + points("--lang", [
    ("内置三种语言", "英文、简体中文、繁体中文直接写在管理器里，不装任何语言文件也能用。"),
    ("语言下拉", "在设置页工具行选语言，选中立刻生效。新放进去的语言文件点刷新就能选。"),
    ("模组名称也能翻", "在概览里双击模组名称、简介或快捷键说明，直接改成你习惯的译名。"),
    ("显示原文", "需要对照时一键切回原文。"),
    ("一键导出待翻文本", "导出全部文字，或者只导出还没翻的。可以只导出勾选的几个模组。"),
    ("导入补翻", "翻好的导出文件点「导入补翻」，有译文的行直接合进当前语言文件，旧文件先备份。"),
    ("正则规则", "只差数字或名字的句子可以写成一条正则，比如 <code>r:\"^(\\d+) players$\"=$1 名玩家</code>。格式和 XUnity 词典一样，能直接搬过来。"),
    ("跟着整合包走", "整合包作者补好的译文随包一起发给玩家，玩家导入时可以选显示语言。"),
    ("不需要 XUnity", "模组设置页的选项名、分区、下拉选项也由管理器自己翻译。装了 XUnity 并开着在线机翻时会提醒，可以一键关掉。"),
]) + """
</section>
<section>
  <h2>一句话从哪里来</h2>
  <p class="muted">界面上每一句都按这个顺序找，找到就停。</p>
  <ul class="steps" style="--c:var(--lang)">
    <li><span><b>个人词典</b>：你自己改的、补翻的。放在 <code>config/ModConfigEnhance/language/</code>。</span></li>
    <li><span><b>作者词典</b>：跟着整合包来的。</span></li>
    <li><span><b>内置</b>：管理器自己的界面文字，中英繁三份。</span></li>
    <li><span><b>英文原文</b>：哪都没找到就显示模组作者写的原文。</span></li>
  </ul>
</section>
<section>
  <h2>补翻是怎么转一圈的</h2>
  <ul class="steps" style="--c:var(--lang)">
    <li><span>在设置页点「导出翻译」或「补漏翻」，待翻文本写到 <code>config/ModConfigEnhance/output/translation/</code>。</span></li>
    <li><span>只填每行等号右边，保留 <code>\\n</code>、<code>{0}</code> 这类占位符。</span></li>
    <li><span>用 UTF-8 保存，点「导入补翻」选这个文件，填好的行合进当前语言文件。</span></li>
    <li><span>发布整合包时勾选这种语言，译文就跟着包一起发出去。</span></li>
  </ul>
</section>
""")


def main():
    for file, (title, body) in PAGES.items():
        io.open(os.path.join(HERE, file), "w", encoding="utf-8", newline="\n").write(shell(file, title, body))
    print("wrote", len(PAGES), "pages")


if __name__ == "__main__":
    main()
