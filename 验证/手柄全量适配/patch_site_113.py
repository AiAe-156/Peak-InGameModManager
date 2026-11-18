# -*- coding: utf-8 -*-
"""1.1.3 intro page: modifier is a choice (default none), not fixed LT."""
import io, os
HERE = os.path.dirname(os.path.abspath(__file__))
p = os.path.abspath(os.path.join(HERE, '..', '..', '文档', '介绍页', 'build.py'))
s = io.open(p, encoding='utf-8').read()
def rep(a, b):
    global s
    assert s.count(a) == 1, (a[:80], s.count(a)); s = s.replace(a, b)
rep("""const MODS=[['none','无'],['rt','按住 RT'],['lt','按住 LT（局内）']];""", """const MODS=[['none','无'],['rt','按住 RT'],['lt','按住修饰键（局内，以 LT 为例）']];""")
rep("""  a:['LT + A：你分配的模组快捷键',P,'在快捷键页给只认键盘的快捷键按 A，再按住 LT 按一个键即可分配。组合期间游戏不会把这个键当成跳跃。'],""",
    """  a:['LT + A：你分配的模组快捷键',P,'快捷键页顶部选好修饰键（可选 LT / RT / LB / RB / Back / Start / RS / LS，默认无），在只认键盘的快捷键的手柄列按 A，先按住修饰键再按一个键即可。组合期间游戏不会把这个键当成跳跃。不用修饰键也行，直接录单键。'],""")
rep(""" if(scene==='play'&&mod==='lt')t='按住 LT 是管理器的组合键层：在快捷键页给只认键盘的模组快捷键分配「LT + 某键」，局内按住 LT 再按那个键就等于按下模组的键盘快捷键。只有 LT 一个修饰键。';""",
    """ if(scene==='play'&&mod==='lt')t='修饰键是可选的：在快捷键页顶部选一个（默认「无」），给只认键盘的模组快捷键录「修饰键 + 某键」或单键，局内按下就等于按下模组的键盘快捷键。同一时间只有一个修饰键；LT + RB / LT + LB 固定为展开 / 收起全部分组。';""")
rep(""" else if(mod==='lt')t='LT 组合键只在局内生效，菜单里按住 LT 没有作用。';""", """ else if(mod==='lt')t='手柄组合键只在局内生效；菜单里只有 LT + RB / LT + LB 展开 / 收起全部分组。';""")
rep("""    ("按住 RT 是功能层", "RT 在游戏菜单里没有用处，按住它时方向键变成跳分组、翻页，不会顺带移动焦点。"),""",
    """    ("按住 RT 是功能层", "RT 在游戏菜单里没有用处，按住它时方向键变成跳分组、翻页，不会顺带移动焦点。"),
    ("手柄也能按模组快捷键", "快捷键页有常驻的手柄列。只认键盘的模组快捷键，可以给它录一个手柄键或「修饰键 + 键」，局内直接用手柄按。修饰键在页面顶部选，默认不用。"),""")
io.open(p, 'w', encoding='utf-8', newline='\n').write(s)
print('ok')
