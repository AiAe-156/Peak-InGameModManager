# -*- coding: utf-8 -*-
"""生成游戏内模组管理器介绍站（7 个静态页）。共用顶栏、页脚和上下页；每页正文写在下面的 PAGES 里。
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
    ("gamepad.html", "手柄操作", "--pin"),
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


# —— 手柄操作（规划中）——
PAGES["gamepad.html"] = ("手柄操作", head("手柄操作 · 规划中", "--pin", "用手柄也能管好模组",
    "按游戏自己的手柄键位来：A 确认、B 返回、LB / RB 切页签。每一格都能单独选中，鼠标的悬停提示换成贴着选中格子弹出。") + """
<style>
  .gp-note { margin-top: 18px; }
  .gp-bar { display: flex; flex-wrap: wrap; gap: 10px 22px; align-items: center; margin: 22px 0 12px; }
  .gp-seg { display: inline-flex; background: var(--paper); border: 1px solid var(--line); border-radius: 9px; overflow: hidden; }
  .gp-seg button { background: none; border: 0; border-right: 1px solid var(--line); padding: 6px 13px; font: 14px var(--sans); color: var(--ink); cursor: pointer; }
  .gp-seg button:last-child { border-right: 0; }
  .gp-seg button.on { background: var(--navy); color: #fff; }
  .gp-seg-label { font-size: 13px; color: var(--ink-2); margin-right: 6px; }
  .gp-legend { display: flex; flex-wrap: wrap; gap: 6px 16px; font-size: 13px; color: var(--ink-2); }
  .gp-legend i { display: inline-block; width: 11px; height: 11px; border-radius: 3px; margin-right: 5px; vertical-align: -1px; }
  .gp-grid { display: grid; grid-template-columns: minmax(0, 1fr) 320px; gap: 18px; align-items: start; }
  .gp-pad { background: var(--navy); border-radius: 14px; padding: 10px; }
  .gp-pad svg { width: 100%; height: auto; display: block; }
  .gp-pad .btn { cursor: pointer; transition: filter .12s; }
  .gp-pad .btn:hover { filter: brightness(1.35); }
  .gp-detail { background: var(--paper); border-radius: 12px; padding: 16px 18px; }
  .gp-detail h3 { margin: 0 0 2px; }
  .gp-detail .k { font-size: 13px; color: var(--ink-2); margin-top: 12px; }
  .gp-detail p { margin: 2px 0; font-size: 15px; }
  .gp-detail .why { font-size: 14px; color: var(--ink-2); }
  .gp-tag { display: inline-block; font-size: 12px; padding: 0 7px; border-radius: 5px; margin-left: 6px; color: #10202e; vertical-align: 1px; }
  .gp-table { width: 100%; border-collapse: collapse; font-size: 14px; background: var(--paper); border-radius: 12px; overflow: hidden; margin-top: 14px; }
  .gp-table th, .gp-table td { border-bottom: 1px solid var(--line); padding: 8px 10px; text-align: left; vertical-align: top; }
  .gp-table th { background: #dbe3e8; font-weight: 700; }
  .gp-table tr:last-child td { border-bottom: 0; }
  .gp-table td.ok { background: #e2f0d6; } .gp-table td.mid { background: #f6e8d2; } .gp-table td.no { background: #f5dcd8; } .gp-table td.na { color: var(--ink-2); }
  .gp-mockwrap { display: grid; grid-template-columns: minmax(0, 1fr) 280px; gap: 18px; align-items: start; margin-top: 14px; }
  .gp-mock { background: #16283a; border-radius: 14px; padding: 10px; color: #dfdac2; position: relative; font-size: 14px; }
  .gp-mhead, .gp-mrow { display: grid; grid-template-columns: 46px 34px minmax(0, 1fr) 38px 38px 38px 104px minmax(0, .8fr); gap: 4px; align-items: center; padding: 0 8px; }
  .gp-mhead { height: 32px; color: #9fb3c6; font-size: 13px; }
  .gp-mgroup { height: 32px; display: flex; align-items: center; gap: 8px; padding-left: 12px; position: relative; font-size: 14px; }
  .gp-mgroup .rail { position: absolute; left: 0; top: 0; bottom: 0; width: 3px; }
  .gp-mrow { height: 42px; border-top: 1px solid #203a52; }
  .gp-mrow.cur { background: #1f3a55; }
  .gp-cell { position: relative; height: 32px; display: flex; align-items: center; padding: 0 6px; border-radius: 6px; white-space: nowrap; overflow: hidden; }
  .gp-cell.focus { outline: 2px solid #f4ead2; outline-offset: -1px; background: rgba(244,234,210,.07); }
  .gp-cell.ico { justify-content: center; color: #d9d2bc; }
  .gp-cell.dim { color: #8ea3b6; font-size: 13px; }
  .gp-chk { width: 18px; height: 18px; border: 2px solid #c9c1a8; border-radius: 4px; margin: auto; display: grid; place-items: center; font-size: 13px; line-height: 1; color: #b9e08e; }
  .gp-orig { color: #8ea3b6; font-size: 12px; margin-left: 6px; }
  .gp-lane { position: absolute; right: 6px; top: 3px; display: flex; flex-direction: row-reverse; gap: 2px; }
  .gp-lane span { width: 8px; height: 8px; border-radius: 2px; }
  .gp-prompt { display: flex; gap: 16px; justify-content: flex-end; padding: 8px 10px 2px; font-size: 13px; color: #d8d2c2; border-top: 1px solid #203a52; margin-top: 4px; }
  .gp-g { display: inline-grid; place-items: center; width: 19px; height: 19px; border-radius: 50%; font-size: 11px; font-weight: 700; color: #111; margin-right: 4px; vertical-align: -4px; }
  .gp-g.sq { border-radius: 4px; width: 26px; }
  .gp-ga { background: #7cc36a; } .gp-gb { background: #e0605a; } .gp-gx { background: #5aa0e0; } .gp-gy { background: #e8c84a; } .gp-gs { background: #c9c9c9; }
  .gp-floats { position: absolute; display: none; gap: 8px; z-index: 3; }
  .gp-float { color: #e8e3cf; background: #0d1823; border: 1px solid #4b6176; border-radius: 9px; padding: 9px 11px; font-size: 13px; line-height: 1.6; box-shadow: 0 10px 24px rgba(0,0,0,.45); width: 270px; }
  .gp-float b { color: #fff; }
  .gp-float .h { font-size: 12px; color: #9fb3c6; margin-bottom: 3px; }
  .gp-float .sw { display: inline-block; width: 9px; height: 9px; border-radius: 2px; margin-right: 6px; vertical-align: 0; }
  .gp-menu { position: absolute; display: none; z-index: 4; color: #e8e3cf; background: #0d1823; border: 1px solid #8a9aa8; border-radius: 10px; padding: 8px; width: 300px; box-shadow: 0 12px 30px rgba(0,0,0,.55); }
  .gp-menu .t { font-weight: 700; padding: 2px 8px 6px; color: #fff; }
  .gp-menu .it { padding: 5px 8px; border-radius: 6px; }
  .gp-menu .it.focus { outline: 2px solid #f4ead2; background: rgba(244,234,210,.07); }
  .gp-menu .tip { font-size: 12px; color: #9fb3c6; padding: 6px 8px 0; }
  .gp-ctl { background: var(--paper); border-radius: 12px; padding: 14px 16px; }
  .gp-dpad { display: grid; grid-template-columns: repeat(3, 44px); grid-template-rows: repeat(3, 36px); gap: 4px; margin-top: 6px; }
  .gp-ctl button { background: #fff; border: 1px solid var(--line); border-radius: 7px; font: 14px var(--sans); color: var(--ink); cursor: pointer; }
  .gp-face { display: flex; flex-wrap: wrap; gap: 6px; margin-top: 10px; }
  .gp-face button { padding: 5px 10px; }
  .gp-log { font-size: 13px; color: var(--ink-2); margin-top: 10px; min-height: 42px; }
  @media (max-width: 980px) { .gp-grid, .gp-mockwrap { grid-template-columns: 1fr; } }
</style>

<section style="padding-top:28px">
  <div class="callout gp-note"><b>规划中，还没有发布。</b>这一页是手柄适配的键位草案，按游戏自己的手柄绑定来定，界面以以后的游戏内实装为准。点手柄上的按键或文字可以看细节。</div>
  <div class="gp-bar">
    <div><span class="gp-seg-label">界面</span><span class="gp-seg" id="gp-scene"></span></div>
    <div><span class="gp-seg-label">显示</span><span class="gp-seg" id="gp-layer"></span></div>
    <div><span class="gp-seg-label">修饰键</span><span class="gp-seg" id="gp-mod"></span></div>
  </div>
  <div class="gp-legend">
    <span><i style="background:#8ea0ad"></i>游戏原生</span>
    <span><i style="background:#6fa8dc"></i>已经支持</span>
    <span><i style="background:#9fd36a"></i>规划新增</span>
    <span><i style="background:#f0a050"></i>能用但有限制</span>
    <span><i style="background:#e0605a"></i>不用（会冲突）</span>
    <span><i style="background:#5b6b78"></i>空闲</span>
  </div>
  <div class="gp-grid" style="margin-top:12px">
    <div><div class="gp-pad"><svg id="gp-svg" viewBox="0 0 1200 470"></svg></div><p class="muted" id="gp-note2" style="display:none;margin-top:8px;font-size:14px"></p></div>
    <div class="gp-detail" id="gp-detail"><h3>点一个按键</h3><p class="why">左边手柄上的按键和文字都能点。「局内」是游戏里的操作和模组占用的键；其余几页是管理器界面。</p></div>
  </div>
</section>

<section>
  <h2>基本规则</h2>
""" + points("--pin", [
    ("沿用游戏的键位", "A 确认、B 返回、十字键和左摇杆移动、LB / RB 切换页签、Start 暂停，和游戏自己的菜单一致。"),
    ("每一列都能单独选中", "启用方框、齿轮、主页、文件夹、版本都是独立的格子，选中哪格按 A 就做哪格的事，不设「一键启停」这类跨列快捷键。"),
    ("Y 打开更多操作", "鼠标要双击或右键才能打开的菜单，手柄按 Y。"),
    ("右摇杆只滚动", "右摇杆滚动列表，焦点留在原处；焦点滚出画面后，再按方向键会先回到画面里。"),
    ("按住 RT 是功能层", "RT 在游戏菜单里没有用处，按住它时方向键变成跳分组、翻页，不会顺带移动焦点。"),
    ("手柄也能按模组快捷键", "快捷键页有常驻的手柄列。只认键盘的模组快捷键，可以给它录一个手柄键或「修饰键 + 键」，局内直接用手柄按。修饰键在页面顶部选，默认不用。"),
    ("作者模式用键鼠", "多选、拖动排序、拖进分组这些编辑操作仍然用鼠标完成。"),
]) + """
</section>

<section>
  <h2>选中名称时显示什么</h2>
  <p class="muted">和鼠标悬停一样弹出提示框，只是贴着选中的格子出现，不跟着鼠标走。名称格一次弹两张并排的卡片：左边是依赖，右边解释这一行的角标。角标本身很小，不单独选中，名称格就是它们的入口；要处理某个角标（看变动并标为已读、看版本、看冲突建议），在名称上按 A 或在这一行按 Y，从菜单里选。</p>
  <div class="gp-mockwrap">
    <div class="gp-mock" id="gp-mock">
      <div class="gp-mhead"><div>启用</div><div>#</div><div>名称</div><div style="text-align:center">⚙</div><div style="text-align:center">⌂</div><div style="text-align:center">▤</div><div>版本</div><div>简介</div></div>
      <div id="gp-rows"></div>
      <div class="gp-prompt" id="gp-prompt"></div>
      <div class="gp-floats" id="gp-floats"></div>
      <div class="gp-menu" id="gp-menu"></div>
    </div>
    <div class="gp-ctl">
      <div class="gp-seg-label">模拟手柄（也可以用键盘方向键和 A / B / Y）</div>
      <div class="gp-dpad">
        <span></span><button data-k="up">↑</button><span></span>
        <button data-k="left">←</button><span></span><button data-k="right">→</button>
        <span></span><button data-k="down">↓</button><span></span>
      </div>
      <div class="gp-face">
        <button data-k="a"><span class="gp-g gp-ga">A</span>确认</button>
        <button data-k="y"><span class="gp-g gp-gy">Y</span>更多</button>
        <button data-k="b"><span class="gp-g gp-gb">B</span>返回</button>
        <button data-k="rtdown"><span class="gp-g gp-gs sq">RT</span>+↓ 下一分组</button>
        <button data-k="rbtab"><span class="gp-g gp-gs sq">RB</span>下一页签</button>
      </div>
      <div class="gp-log" id="gp-log"></div>
    </div>
  </div>
</section>

<section>
  <h2>组合键能放在哪</h2>
  <p class="muted">只用游戏菜单里空着、又不会撞上常见模组的键。</p>
  <table class="gp-table" id="gp-matrix"></table>
</section>

<section>
  <h2>手柄做不到的</h2>
  <div class="two">
    <div><ul class="steps" style="--c:var(--mods)">
      <li><span><b>打字</b>：普通桌面模式下改名、搜索还要用键盘。在 Steam 大屏幕模式和 Steam Deck 上，选中输入框按 A 会弹出 Steam 屏幕键盘。</span></li>
      <li><span><b>打开文件夹以后</b>：资源管理器和外部程序里的操作要用键鼠。</span></li>
      <li><span><b>作者模式的编辑</b>：多选、拖动排序、拖进分组、写介绍用键鼠。</span></li>
      <li><span><b>日志逐字选择</b>：改为按条选中再复制。</span></li>
    </ul></div>
    <div class="callout"><b>会撞键的模组：</b>K 键掉线重连（peakReconnect）不在大厅时按 LT 就会重连，所以管理器不用 LT；物品生成器增强用 View + RT 开窗口，所以 RT 功能层里不用 View；背队友（Piggyback）在局内用 LB + RB 放人，所以不用双肩同按。</div>
  </div>
</section>

<script>
(function(){
const C={native:'#8ea0ad',cur:'#6fa8dc',new:'#9fd36a',warn:'#f0a050',bad:'#e0605a',idle:'#5b6b78'};
const TAGN={native:'原生',cur:'已支持',new:'规划',warn:'有限制',bad:'不用',idle:'空闲'};
const SCENES=[['play','局内（游戏中）'],['list','模组列表'],['hotkey','快捷键页'],['dialog','弹窗与菜单'],['settings','模组设置页']];
const LAYERS=[['plan','规划'],['now','现在'],['native','游戏原生']];
const MODS=[['none','无'],['rt','按住 RT'],['lt','按住修饰键（局内，以 LT 为例）']];
let scene='play',layer='plan',mod='none';
const N='native',U='cur',P='new',W='warn',X='bad',I='idle';
const B={
 lt:{s:'r',x:380,y:28,w:84,h:40,r:12,n:'LT 左扳机'}, rt:{s:'r',x:736,y:28,w:84,h:40,r:12,n:'RT 右扳机'},
 lb:{s:'r',x:360,y:80,w:124,h:24,r:10,n:'LB 左肩键'}, rb:{s:'r',x:716,y:80,w:124,h:24,r:10,n:'RB 右肩键'},
 ls:{s:'c',x:430,y:205,r:38,n:'左摇杆'}, l3:{s:'c',x:430,y:205,r:14,n:'L3 按下左摇杆'},
 rs:{s:'c',x:690,y:290,r:38,n:'右摇杆'}, r3:{s:'c',x:690,y:290,r:14,n:'R3 按下右摇杆'},
 dpad:{s:'d',x:510,y:290,n:'十字键'},
 y:{s:'c',x:770,y:168,r:17,n:'Y',t:'Y',f:'#e8c84a'}, x:{s:'c',x:735,y:203,r:17,n:'X',t:'X',f:'#5aa0e0'},
 b:{s:'c',x:805,y:203,r:17,n:'B',t:'B',f:'#e0605a'}, a:{s:'c',x:770,y:238,r:17,n:'A',t:'A',f:'#7cc36a'},
 view:{s:'r',x:548,y:196,w:30,h:16,r:8,n:'View / Select'}, menu:{s:'r',x:622,y:196,w:30,h:16,r:8,n:'Menu / Start'}
};
const L={lt:[330,46,'end'],lb:[330,94,'end'],ls:[330,168,'end'],l3:[330,228,'end'],dpad:[330,322,'end'],view:[330,410,'end'],
 rt:[870,46,'start'],rb:[870,94,'start'],y:[870,144,'start'],x:[870,186,'start'],b:[870,228,'start'],a:[870,270,'start'],rs:[870,322,'start'],r3:[870,366,'start'],menu:[870,410,'start']};
const NATIVE={ls:['移动焦点',N],rs:['也移动焦点',N,'游戏把右摇杆和左摇杆、十字键一起绑在「导航」上。'],dpad:['移动焦点',N],a:['确认',N],
 x:['确认（只在游戏自己的窗口里）',N],b:['返回',N],lb:['上一个页签',N],rb:['下一个页签',N],menu:['暂停；在菜单里等于返回',N],
 view:['调试菜单（正式版无效）',N],lt:['菜单里没用',I],rt:['菜单里没用',I],y:['菜单里没用',I],l3:['菜单里没用',I],r3:['菜单里没用',I]};
// 局内：游戏自己的 Default 动作表（现装 PEAK 3.1 globalgamemanagers.assets）+ 读手柄键的模组。
// 管理器在局内不接任何手柄键，所以「规划」「现在」都等于游戏原生加模组占用。
const PLAY={
 none:{ls:['移动',N,'键盘 WASD。'],rs:['视角',N,'键盘为鼠标。'],dpad:['↑ 表情 · ↓ 收起手里的物品 · ←→ 切换物品栏',N,'观战时 ←→ 切换观战对象。键盘：R 表情、Tab 收起、1–9 选格子。'],
  a:['跳跃',N,'键盘空格。'],x:['交互 / 拾取（按住 = 长按交互）',N,'键盘 E。'],y:['丢弃',N,'键盘 Q。'],b:['蹲下（切换）',N,'键盘左 Ctrl（按住）。'],
  lb:['物品栏向后滚动 · 按住 + RB = 按键说话',N,'按键说话是游戏自己的组合：先按住 LB 再按 RB，或先按住 RB 再按 LB。键盘 G / V。'],rb:['物品栏向前滚动 · 按住 + LB = 按键说话',N,'键盘 T / V。'],
  lt:['使用 / 投掷（副）',W,'游戏的副使用键（鼠标右键）。K 键掉线重连（peakReconnect）也用 LT：不在 Steam 大厅里时按 LT 立刻重连上一个房间——比如被踢回主菜单后。'],
  rt:['使用（主）',W,'游戏的主使用键（鼠标左键）。物品生成器增强：按住 View 再按 RT 打开它的窗口。'],
  l3:['疾跑（切换）',N,'键盘左 Shift（按住）。'],r3:['标记（Ping）',N,'鼠标中键。'],
  view:['调试菜单（正式版无效）· 按住 + RT = 物品生成器',W,'物品生成器增强的手柄开窗组合，可在它的配置里改。'],menu:['暂停菜单',N,'键盘 Esc。从这里进入管理器。']},
 rt:{view:['按住 View 再按 RT：物品生成器窗口',W,'来自物品生成器增强（调试类模组），配置项 Controller Chord Modifier Path / Controller Toggle Path。'],rt:['（按住中）',P]},
 lt:{lt:['（按住中）· 游戏副使用照常',W,'LT 是游戏的副使用 / 投掷，按住时照常触发；K 键掉线重连在大厅外按 LT 会重连。'],
  a:['LT + A：你分配的模组快捷键',P,'快捷键页顶部选好修饰键（可选 LT / RT / LB / RB / Back / Start / RS / LS，默认无），在只认键盘的快捷键的手柄列按 A，先按住修饰键再按一个键即可。组合期间游戏不会把这个键当成跳跃。不用修饰键也行，直接录单键。'],
  b:['LT + B：可分配',P],x:['LT + X：可分配',P],y:['LT + Y：可分配',P],lb:['LT + LB：可分配',P],rb:['LT + RB：可分配',P],rt:['LT + RT：可分配',P],
  dpad:['LT + ↑↓←→：各可分配',P],l3:['LT + L3：可分配',P],r3:['LT + R3：可分配',P],view:['LT + View：可分配',P],menu:['LT + Menu：可分配',P],
  ls:['移动（不变）',N],rs:['视角（不变）',N]},
};
const NOW={
 list:{dpad:['移动焦点，上下保持在同一列',U],ls:['同十字键',U],rs:['跟游戏一样移动焦点',N],a:['按下选中的按钮；名称上没反应',W,'启用方框、齿轮、主页、文件夹、版本都能按；名称能选中但按 A 没有反应，鼠标双击才有的菜单手柄打不开。'],
  b:['逐层关闭，再离开页面',U],x:['—',I],y:['—',I],lb:['—',I],rb:['—',I],lt:['—',I],rt:['—',I],view:['—',I],menu:['游戏原生',N],l3:['—',I],r3:['—',I]},
 hotkey:{dpad:['移动焦点',U],ls:['同十字键',U],rs:['滚动列表',W,'可能同时也在移动焦点，待实机确认。'],a:['录制按键（按住 0.55 秒）/ 改说明',U],y:['按键菜单',U,'已经能用，但界面上没有提示。'],b:['取消录制、关菜单、返回',U],
  x:['—',I],lb:['—',I],rb:['—',I],lt:['—',I],rt:['—',I],view:['—',I],menu:['游戏原生',N],l3:['—',I],r3:['—',I]},
 dialog:{dpad:['移动焦点（到头绕回）',U],ls:['同十字键',U],rs:['—',X,'长正文现在没法用手柄滚动。'],a:['确认',U],b:['关闭',U],x:['—',I],y:['—',I],lb:['—',I],rb:['—',I],lt:['—',I],rt:['—',I],view:['—',I],menu:['游戏原生',N],l3:['—',I],r3:['—',I]},
 settings:{dpad:['左栏 / 右栏两级移动',U],ls:['同十字键',U],rs:['跟游戏一样移动焦点',N],a:['模组 → 分区 → 右栏；滑条先按 A 再调',U],b:['右栏 → 左栏 → 返回',U],lb:['上一个模组',U],rb:['下一个模组',U],
  x:['—',I],y:['—',I],lt:['—',I],rt:['—',I],view:['—',I],menu:['游戏原生',N],l3:['—',I],r3:['—',I]}
};
const PLAN={
 list:{none:{dpad:['移动焦点；按住连跳；选中行整行提亮',P],ls:['同十字键',U],rs:['只滚动，焦点不动',P,'焦点滚出画面后，再按方向键会先回到画面里的同一列。'],
   a:['按下选中的格子；名称上 = 打开这一行的菜单',P,'启用方框按 A 就是勾选 / 取消。名称上按 A 和按 Y 一样打开这一行的菜单。'],
   y:['这一行的菜单',P,'代替双击和右键：更改模组信息、改译名、查看依赖、查看版本、看变动并标为已读、冲突建议。'],
   b:['关菜单 → 离开页面（没保存先问）',U],x:['—',I],lb:['上一个页签（列表 / 介绍 / 快捷键）',P],rb:['下一个页签',P],
   lt:['不用',X,'K 键掉线重连（peakReconnect）不在大厅时按 LT 就会重连，在主菜单打开管理器就会误触。'],
   rt:['按住 = 功能层',P,'按住时方向键不再移动焦点，改成跳分组和翻页。'],view:['显示 / 隐藏按键提示',P],menu:['游戏原生',N],
   l3:['折叠 / 展开所在分组',P],r3:['跳到搜索框',P,'Steam 大屏幕模式和 Steam Deck 上会弹出屏幕键盘。']},
  rt:{dpad:['↑↓ 上 / 下一个分组 · ←→ 翻页',P],ls:['同十字键',P],rs:['快速滚动',P],y:['所在分组的菜单',P,'新建子分组、编辑分组、移到顶层、删除分组。'],l3:['全部折叠 / 全部展开',P],
   a:['—',I],x:['—',I],b:['—',I],lb:['—',I],rb:['—',I],view:['不用',X,'View + RT 是物品生成器增强开窗口的键。'],menu:['—',I],r3:['—',I],lt:['—',I]}},
 hotkey:{none:{dpad:['移动焦点；选中行整行提亮',P],ls:['同十字键',U],rs:['只滚动，焦点不动',P],a:['录制 / 改说明',U],y:['按键菜单（补上提示）',U],b:['取消录制、关菜单、返回',U],x:['—',I],
   lb:['上一个页签',P],rb:['下一个页签',P],lt:['不用',X],rt:['按住 = 功能层',P],view:['显示 / 隐藏按键提示',P],menu:['游戏原生',N],l3:['折叠 / 展开分组',P],r3:['跳到搜索框',P]},
  rt:{dpad:['↑↓ 上 / 下一个分组 · ←→ 翻页',P],ls:['同十字键',P],rs:['快速滚动',P],y:['分组菜单',P],l3:['全部折叠 / 全部展开',P],a:['—',I],x:['—',I],b:['—',I],lb:['—',I],rb:['—',I],view:['不用',X],menu:['—',I],r3:['—',I],lt:['—',I]}},
 dialog:{none:{dpad:['移动焦点（到头绕回）',U],ls:['同十字键',U],rs:['滚动长正文',P,'问题报告里左右推可以横向滚动。'],a:['确认',U],b:['关闭（有上层先关上层）',P,'比如日志窗的导出选项，按 B 只关选项，不关整个日志窗。'],
   x:['—',I],y:['窗口里的更多操作',P],lb:['多页窗口切页',P,'管理中心、更新队列。'],rb:['多页窗口切页',P],lt:['不用',X],rt:['按住 = 功能层',P],view:['显示 / 隐藏按键提示',P],menu:['游戏原生',N],l3:['—',I],r3:['跳到窗口里的输入框',P]},
  rt:{dpad:['翻页 / 跳到首尾',P],ls:['同十字键',P],rs:['快速滚动',P],a:['—',I],x:['—',I],y:['—',I],b:['—',I],lb:['—',I],rb:['—',I],view:['不用',X],menu:['—',I],l3:['—',I],r3:['—',I],lt:['—',I]}},
 settings:{none:{dpad:['不变',U],ls:['同十字键',U],rs:['滚动右栏，焦点不动',P],a:['不变',U],b:['不变',U],lb:['上一个模组（不变）',U],rb:['下一个模组（不变）',U],x:['—',I],
   y:['这一项的菜单',P,'复制键名、跳回概览里的这个模组。'],lt:['不用',X],rt:['按住 = 功能层',P],view:['显示 / 隐藏按键提示',P],menu:['游戏原生',N],l3:['—',I],r3:['跳到搜索框',P]},
  rt:{dpad:['↑↓ 上 / 下一个分区',P],ls:['同十字键',P],rs:['快速滚动',P],lb:['回到模组概览',P],a:['—',I],x:['—',I],y:['—',I],b:['—',I],rb:['—',I],view:['不用',X],menu:['—',I],l3:['—',I],r3:['—',I],lt:['—',I]}}
};
// 显示层与修饰键可以任意组合。游戏原生与「现在」只描述单按；选了「按住 RT」时，
// 原生层在 RT 上标「按住中」，其余键照常显示单按含义（游戏菜单里按住 RT 本身不改变任何键）。
function entry(id){
 if(scene==='play'){const m=(mod!=='none'&&PLAY[mod]&&PLAY[mod][id])?PLAY[mod][id]:PLAY.none[id];return m||['—',I];}
 if(mod==='lt')return id==='lt'?['（只在局内用）',I]:entry0(id);
 if(mod!=='none'&&id===mod)return['（按住中）',layer==='plan'?P:I];
 if(layer==='native')return NATIVE[id];
 if(layer==='now')return NOW[scene][id]||['—',I];
 const m=PLAN[scene][mod]; return m[id]||['—',I];
}
function entry0(id){const save=mod;mod='none';const e=entry(id);mod=save;return e;}
function note(){
 const box=document.getElementById('gp-note2');if(!box)return;
 let t='';
 if(scene==='play'&&mod==='lt')t='修饰键是可选的：在快捷键页顶部选一个（默认「无」），给只认键盘的模组快捷键录「修饰键 + 某键」或单键，局内按下就等于按下模组的键盘快捷键。同一时间只有一个修饰键；LT + RB / LT + LB 固定为展开 / 收起全部分组。';
 else if(scene==='play')t='局内是游戏自己的操作；黄色是有模组同时在用的键。快捷键页的「重复」检查会把这些游戏按键一起算上。';
 else if(mod==='lt')t='手柄组合键只在局内生效；菜单里只有 LT + RB / LT + LB 展开 / 收起全部分组。';
 else if(mod==='rt'&&layer!=='plan')t='游戏菜单里 RT 没有任何作用，所以「游戏原生」「现在」两层按住 RT 时其余键不变；组合键只在「规划」层出现。';
 box.textContent=t;box.style.display=t?'block':'none';
}
const svg=document.getElementById('gp-svg');
function el(tag,a,p){const e=document.createElementNS('http://www.w3.org/2000/svg',tag);for(const k in a)e.setAttribute(k,a[k]);(p||svg).appendChild(e);return e;}
function draw(){
 svg.innerHTML='';
 el('path',{d:'M470,110 Q600,92 730,110 Q840,120 865,215 L905,360 Q920,440 850,450 Q800,455 765,395 L725,335 Q600,350 475,335 L435,395 Q400,455 350,450 Q280,440 295,360 L335,215 Q360,120 470,110 Z',fill:'#24486a',stroke:'#3d6488','stroke-width':2});
 for(const id in L){const g=B[id],e=entry(id),col=C[e[1]],p=L[id];
  const cx=g.s==='r'?g.x+g.w/2:g.x,cy=g.s==='r'?g.y+g.h/2:g.y;
  el('line',{x1:p[0]+(p[2]==='end'?6:-6),y1:p[1]-4,x2:cx,y2:cy,stroke:col,'stroke-width':1.2,'stroke-opacity':.6});
  const t=el('text',{x:p[0],y:p[1],'text-anchor':p[2],class:'btn'});
  const a=el('tspan',{x:p[0],fill:'#a9bccd','font-size':14},t);a.textContent=g.n;
  const b=el('tspan',{x:p[0],dy:19,fill:col,'font-size':16},t);b.textContent=e[0];
  t.addEventListener('click',()=>show(id));}
 for(const id of ['lt','rt','lb','rb','ls','rs','dpad','y','x','b','a','view','menu','l3','r3']){const g=B[id],e=entry(id),col=C[e[1]];let s;
  if(g.s==='r')s=el('rect',{x:g.x,y:g.y,width:g.w,height:g.h,rx:g.r,fill:'#1b3d5c',stroke:col,'stroke-width':3,class:'btn'});
  else if(g.s==='c')s=el('circle',{cx:g.x,cy:g.y,r:g.r,fill:g.f||(id==='l3'||id==='r3'?'#16334f':'#1b3d5c'),'fill-opacity':g.f?.9:1,stroke:col,'stroke-width':3,class:'btn'});
  else s=el('path',{d:`M${g.x-12},${g.y-38}h24v26h26v24h-26v26h-24v-26h-26v-24h26z`,fill:'#1b3d5c',stroke:col,'stroke-width':3,class:'btn'});
  s.addEventListener('click',()=>show(id));
  if(g.t){const t=el('text',{x:g.x,y:g.y+5,'text-anchor':'middle','font-size':14,'font-weight':700,fill:'#121212'});t.textContent=g.t;t.style.pointerEvents='none';}}
}
function tag(e){return `<span class="gp-tag" style="background:${C[e[1]]}">${TAGN[e[1]]}</span>`;}
function why(e){return e[2]?`<p class="why">${e[2]}</p>`:'';}
function show(id){
 if(scene==='play'){const e=PLAY.none[id]||['—',I],r=PLAY.rt[id];
  document.getElementById('gp-detail').innerHTML=`<h3>${B[id].n}</h3><div class="why">局内（游戏中）</div><div class="k">游戏与模组</div><p>${e[0]}${tag(e)}</p>${why(e)}${r&&r[0].indexOf('按住中')<0?`<div class="k">按住 RT</div><p>${r[0]}${tag(r)}</p>${why(r)}`:''}`;return;}
 const n=NATIVE[id],c=NOW[scene][id]||['—',I],p=PLAN[scene].none[id]||['—',I],r=PLAN[scene].rt[id];
 document.getElementById('gp-detail').innerHTML=`<h3>${B[id].n}</h3><div class="why">${SCENES.find(s=>s[0]===scene)[1]}</div>
 <div class="k">规划</div><p>${p[0]}${tag(p)}</p>${why(p)}
 ${r&&r[1]!==I?`<div class="k">按住 RT</div><p>${r[0]}${tag(r)}</p>${why(r)}`:''}
 <div class="k">现在</div><p>${c[0]}${tag(c)}</p>${why(c)}
 <div class="k">游戏原生</div><p>${n[0]}${tag(n)}</p>${why(n)}`;
}
function seg(id,items,get,set){const box=document.getElementById(id);box.innerHTML='';
 for(const [k,v] of items){const b=document.createElement('button');b.textContent=v;if(get()===k)b.className='on';b.onclick=()=>{set(k);render();};box.appendChild(b);}}
function render(){seg('gp-scene',SCENES,()=>scene,v=>scene=v);seg('gp-layer',LAYERS,()=>layer,v=>{layer=v;});seg('gp-mod',MODS,()=>mod,v=>{mod=v;});
 document.getElementById('gp-layer').parentElement.style.opacity=scene==='play'?.45:1;draw();note();}
render();

const MX=[['按住的键','方向键','A','Y','另一边的键','结论'],
 ['RT','ok:跳分组 / 翻页','na:不用','ok:分组菜单','mid:View + RT 是物品生成器','<b>采用</b>：游戏菜单里没用，常见模组只占了 View + RT'],
 ['LT','no:','no:','no:','no:','不用：K 键掉线重连单按 LT 就会触发'],
 ['LB / RB','mid:可以','mid:可以','mid:可以','no:LB + RB 是背队友放人','不用：单按要切页签'],
 ['View','mid:可以','mid:可以','mid:可以','no:View + RT 冲突','不用：键太小，长按不顺手'],
 ['L3 / R3','na:','na:','na:','na:','不用：按住摇杆再按别的键很难']];
const mt=document.getElementById('gp-matrix');
MX.forEach((r,i)=>{const tr=document.createElement('tr');r.forEach((c,j)=>{const td=document.createElement(i===0||j===0?'th':'td');const m=/^(ok|mid|no|na):(.*)$/.exec(c);
 if(m&&i>0){td.className=m[1];td.innerHTML=m[2]||({ok:'可以',mid:'可以',no:'冲突',na:'—'})[m[1]];}else td.innerHTML=c;tr.appendChild(td);});mt.appendChild(tr);});

// —— 名称列模拟 ——
const ROWS=[
 {group:'核心与基础',color:'#6fa8dc'},
 {name:'游戏内模组管理器',orig:'In-Game Mod Manager',on:true,ver:'1.0.0',desc:'在游戏里管理模组、分组与更新',chips:[],deps:['PEAKLib.ModConfig','PEAKLib.Core','PEAKLib.UI'],by:0},
 {name:'更好的语音修复',orig:'Better Peak Voice Fix',on:true,ver:'1.1.5',desc:'修复语音断连与房间码区服',chips:['unread','update'],deps:['PEAKLib.Core'],by:0},
 {name:'随机地形增强',orig:'RandomTerrainEnhance',on:false,ver:'1.0.4',desc:'随机地形的同步与烘焙修正',chips:['save','restart'],deps:['TerrainRandomiser'],by:1},
 {group:'拓展',color:'#9fd36a'},
 {name:'K键掉线重连',orig:'peakReconnect',on:true,ver:'1.0.2',desc:'按键或手柄键重连上一个房间',chips:['nested'],deps:[],by:0},
 {name:'资源包示例',orig:'SomeAssets',on:true,ver:'0.3.0',desc:'给其他模组用的贴图',chips:['shape'],deps:[],by:2},
];
const CHIP={unread:['#6eaeeb','蓝色','有 2 条未读变动','看变动并标为已读'],update:['#80c47d','绿色','有新版本 1.1.6','查看版本与更新'],save:['#ff8c1f','橙色','改动还没保存：改为停用',''],
 restart:['#ad94f2','紫色','重启游戏后生效',''],nested:['#e0a154','琥珀色','放在另一个模组的文件夹里','跳到外层模组'],shape:['#61c7c2','青色','资源包：没有 DLL','']};
const SLOT={unread:0,update:1,save:2,restart:3,nested:4,shape:5};
const COLS=['chk','name','gear','home','folder','ver'];
const TABS=['模组列表','介绍','快捷键'];let tab=0;
let fr=2,fc=1,menu=null;
const mods=()=>ROWS.map((r,i)=>i).filter(i=>!ROWS[i].group);
function rows(){
 const box=document.getElementById('gp-rows');box.innerHTML='';
 ROWS.forEach((r,i)=>{
  if(r.group){const d=document.createElement('div');d.className='gp-mgroup';d.innerHTML=`<span class="rail" style="background:${r.color}"></span><span>▾</span><span style="color:${r.color}">${r.group}</span>`;box.appendChild(d);return;}
  const lane=[0,1,2,3,4,5].map(k=>{const c=r.chips.find(c=>SLOT[c]===k);return `<span style="background:${c?CHIP[c][0]:'transparent'}"></span>`;}).join('');
  const f=c=>i===fr&&COLS[fc]===c?' focus':'';
  const d=document.createElement('div');d.className='gp-mrow'+(i===fr?' cur':'');
  d.innerHTML=`<div class="gp-cell${f('chk')}"><span class="gp-chk">${r.on?'✓':''}</span></div><div class="gp-cell dim">${mods().indexOf(i)+1}</div>
   <div class="gp-cell${f('name')}" data-c="name">${r.name}<span class="gp-orig">${r.orig}</span><span class="gp-lane">${lane}</span></div>
   <div class="gp-cell ico${f('gear')}">⚙</div><div class="gp-cell ico${f('home')}">⌂</div><div class="gp-cell ico${f('folder')}">▤</div>
   <div class="gp-cell${f('ver')}">${r.ver}${r.chips.includes('update')?' <span style="display:inline-block;width:8px;height:8px;border-radius:2px;background:#80c47d;margin-left:6px"></span>':''}</div><div class="gp-cell dim">${r.desc}</div>`;
  box.appendChild(d);});
 const r=ROWS[fr],col=COLS[fc],fl=document.getElementById('gp-floats');
 let html='';
 if(col==='name'){
  html=`<div class="gp-float"><div class="h">依赖</div><b>${r.name}</b><br>需要：${r.deps.length?r.deps.join('、'):'无'}<br>被 ${r.by} 个模组需要</div>`;
  html+=`<div class="gp-float"><div class="h">角标</div>${r.chips.length?r.chips.map(c=>`<span class="sw" style="background:${CHIP[c][0]}"></span>${CHIP[c][1]}：${CHIP[c][2]}`).join('<br>'):'这一行没有角标'}${r.chips.some(c=>CHIP[c][3])?'<br><span style="color:#9fb3c6">按 A 或 Y 处理</span>':''}</div>`;
 } else {
  const t={chk:r.on?'已启用，按 A 停用（先进草稿）':'已停用，按 A 启用（先进草稿）',gear:'打开这个模组的设置页',home:'打开项目主页 / 商店页',folder:'打开模组所在的文件夹',ver:'版本 '+r.ver+(r.chips.includes('update')?'，有新版本 1.1.6。按 A 查看':'')}[col];
  html=`<div class="gp-float">${t}</div>`;
 }
 fl.innerHTML=html;fl.style.display=menu?'none':'flex';
 const rowEl=box.children[fr],cell=rowEl.children[col==='chk'?0:col==='name'?2:col==='gear'?3:col==='home'?4:col==='folder'?5:6];
 const mock=document.getElementById('gp-mock');
 fl.style.top=(rowEl.offsetTop+rowEl.offsetHeight+2)+'px';
 fl.style.left=Math.max(8,Math.min(cell.offsetLeft+8,mock.clientWidth-(col==='name'?560:290)))+'px';
 const pa={chk:'勾选',name:'菜单',gear:'打开设置',home:'打开主页',folder:'打开文件夹',ver:'查看版本'}[col];
 document.getElementById('gp-prompt').innerHTML=`<span style="margin-right:auto;color:#9fb3c6">页签：${TABS[tab]}</span><span><span class="gp-g gp-ga">A</span>${pa}</span><span><span class="gp-g gp-gy">Y</span>更多</span><span><span class="gp-g gp-gs sq">RT</span>功能</span><span><span class="gp-g gp-gb">B</span>返回</span>`;
 const mn=document.getElementById('gp-menu');
 if(menu){mn.style.display='block';mn.style.top=(rowEl.offsetTop+rowEl.offsetHeight+2)+'px';mn.style.left=(cell.offsetLeft+8)+'px';
  mn.innerHTML=`<div class="t">${menu.title}</div>`+menu.items.map((t,k)=>`<div class="it${k===menu.i?' focus':''}">${t}</div>`).join('')+`<div class="tip">↑↓ 选择　A 执行　B 关闭</div>`;}
 else mn.style.display='none';
}
function log(t){document.getElementById('gp-log').textContent=t;}
function rowMenu(r){
 const items=r.chips.filter(c=>CHIP[c][3]).map(c=>`<span class="gp-g" style="width:9px;height:9px;border-radius:2px;background:${CHIP[c][0]}"></span>${CHIP[c][3]}`);
 return {title:r.name,i:0,items:items.concat(['更改模组信息','改译名','查看依赖','查看版本'])};
}
function key(k){
 const m=mods();let idx=m.indexOf(fr);
 if(menu){
  if(k==='up')menu.i=Math.max(0,menu.i-1);else if(k==='down')menu.i=Math.min(menu.items.length-1,menu.i+1);
  else if(k==='b'){menu=null;log('B：关闭菜单，焦点回到原来的格子');}
  else if(k==='a'){const t=menu.items[menu.i].replace(/<[^>]+>/g,'');if(t==='看变动并标为已读'){const r=ROWS[fr];r.chips=r.chips.filter(c=>c!=='unread');}log('A：'+t);menu=null;}
  rows();return;}
 if(k==='up'&&idx>0)fr=m[idx-1];else if(k==='down'&&idx<m.length-1)fr=m[idx+1];
 else if(k==='left')fc=Math.max(0,fc-1);else if(k==='right')fc=Math.min(COLS.length-1,fc+1);
 else if(k==='a'){const r=ROWS[fr],c=COLS[fc];
  if(c==='chk'){r.on=!r.on;if(!r.chips.includes('save'))r.chips.push('save');log('A：'+(r.on?'启用':'停用')+'，进草稿（名称右上出现橙色角标）');}
  else if(c==='name'){menu=rowMenu(r);log('A（名称）：打开这一行的菜单');}
  else log('A：'+{gear:'打开设置页',home:'打开主页',folder:'打开文件夹（之后的操作要用键鼠）',ver:'打开版本与更新'}[c]);}
 else if(k==='y'){menu=rowMenu(ROWS[fr]);log('Y：这一行的菜单（任何一格都能按）');}
 else if(k==='b')log('B：没有打开的菜单 → 离开页面（有没保存的改动会先问）');
 else if(k==='rtdown'){const next=ROWS.findIndex((r,i)=>i>fr&&r.group);if(next>0){fr=m.find(i=>i>next);log('RT + ↓：跳到下一个分组的第一行，列不变');}else log('RT + ↓：已经是最后一个分组');}
 else if(k==='rbtab'){tab=(tab+1)%TABS.length;log('RB：切到「'+TABS[tab]+'」页签');}
 rows();
}
document.querySelectorAll('#gp-mock ~ .gp-ctl [data-k], .gp-ctl [data-k]').forEach(b=>b.onclick=()=>key(b.dataset.k));
document.getElementById('gp-mock').addEventListener('click',()=>{});
document.addEventListener('keydown',e=>{if(e.target.closest&&e.target.closest('input,textarea'))return;const k={ArrowUp:'up',ArrowDown:'down',ArrowLeft:'left',ArrowRight:'right',a:'a',b:'b',y:'y'}[e.key];
 const r=document.getElementById('gp-mock').getBoundingClientRect();if(k&&r.top<innerHeight&&r.bottom>0){e.preventDefault();key(k);}});
rows();log('焦点在「更好的语音修复」的名称上。');
})();
</script>
""")


def main():
    for file, (title, body) in PAGES.items():
        io.open(os.path.join(HERE, file), "w", encoding="utf-8", newline="\n").write(shell(file, title, body))
    print("wrote", len(PAGES), "pages")


if __name__ == "__main__":
    main()
