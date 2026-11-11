from pathlib import Path
r=Path(__file__).resolve().parent.parent
checks=0
def check(ok,name):
 global checks
 if not ok: raise AssertionError(name)
 checks+=1; print('PASS',name)
def source(p):return (r/p).read_text(encoding='utf-8-sig')
w=source('src/Overview/MceTextWeight.cs');ui=source('src/Overview/OverviewUi.cs');reg=source('src/Overview/Authoring/IntroductionFontRegistry.cs');intro=source('src/Overview/Authoring/IntroductionReadingUi.cs')
check('new Material(source)' in w,'shared game material is cloned before normalization')
check('Balanced.Contains(source)' in w,'balanced material is idempotent')
check('Set("_WeightNormal", 0)' in w and 'Set("_WeightBold", .16f)' in w,'one normal and bold shader policy')
check('Set("_OutlineWidth", 0)' in w and 'DisableKeyword("UNDERLAY_ON")' in w,'outline and shadow removed locally')
check('sub.fontAsset == null' in w,'sprite submesh excluded')
check('sub.sharedMaterial = balanced' in w,'fallback and inline font materials covered')
check('OnPreRenderText' in w and '_base!=Target.fontSharedMaterial' in w,'font/material and mesh changes tracked')
check('MceTextWeight.Apply(caption)' in ui,'native prefab button caption covered')
check('ReadableMaterials' not in ui and 'MceTextWeight.Apply(text)' in ui,'name and ordinary text use shared rule')
check('MceTextWeight.Apply(text)' in intro and '_owned' not in intro,'introduction no longer allocates per paragraph material')
check('font.boldStyle = .8f' not in reg and 'face.boldStyle = game.boldStyle' not in reg,'imported/private font heavy bold inheritance removed')
check('face.normalStyle = 0; face.boldStyle = .16f' in reg,'private game face normalized')
check('font.normalStyle = 0; font.boldStyle = .16f' in reg,'imported font face normalized')
check('if (s.Bold) markup("<b>")' in source('src/Overview/Editor/DocRichText.cs'),'author bold markup preserved')
check('Overview.MceTextWeight.Apply(text)' in source('src/ModConfigDescriptionPanel.cs'),'description panel covered')
check('Overview.MceTextWeight.Apply(text)' in source('src/ModConfigSidebarWidgets.cs'),'sidebar labels covered')
check('MceTextWeight.Apply(button.Label)' in source('src/Overview/Editor/DocToolbar.cs'),'document toolbar covered')
print('PASS',checks,'font policy source-contract checks; visual rendering requires game')
