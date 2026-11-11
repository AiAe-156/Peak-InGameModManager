from pathlib import Path
from datetime import datetime
import json, shutil, subprocess
ROOT=Path(__file__).resolve().parents[3]
HERE=Path(__file__).parent
verification=HERE/'verify_20261029_025014/summary.json'
assert json.loads(verification.read_text(encoding='utf-8'))['status']=='verification-passed'
cmd='$OutputEncoding=[Console]::OutputEncoding=[Text.UTF8Encoding]::new(); $p=@(Get-Process -ErrorAction Stop | Where-Object {$_.ProcessName -in @("PEAK","MCE.UpdateWorker")} | Select-Object ProcessName,Id,Path); ConvertTo-Json -InputObject $p -Compress'
p=subprocess.run(['powershell.exe','-NoProfile','-Command',cmd],capture_output=True,encoding='utf-8',check=True)
if json.loads(p.stdout):raise SystemExit('Running processes; stop without packaging/deployment: '+p.stdout)
now=datetime.now().astimezone();stamp=now.strftime('%Y%m%d_%H%M%S')
run=HERE/('attempt_delivery_'+stamp);run.mkdir(exist_ok=False)
archive=ROOT/'发布'/f'ModConfigEnhance_2.0.0_{stamp}_infrastructure-tree.zip'
backup=ROOT/'备份'/('部署前_基础组件依赖树_'+stamp)/'installed'
plan={'prepared_at':now.isoformat(),'version':'2.0.0','zip':str(archive),'backup':str(backup),'verification':str(verification),'preparation_processes':json.loads(p.stdout)}
(run/f'plan_{stamp}.json').write_text(json.dumps(plan,ensure_ascii=False,indent=2),encoding='utf-8')
relative_zip=archive.relative_to(ROOT).as_posix();relative_backup=backup.relative_to(ROOT).as_posix();relative_run=run.relative_to(ROOT).as_posix()
section=f'''\n\n## 2026-10-29 基础组件依赖树 ReleaseFinal 授权交付（2.0.0）

- 准备时间：{now.isoformat()}；保留所有既有修改，本轮不增加功能、不改生产代码或测试断言。最终 ScopedFix 生产修复限 `src/Updating/ComponentDependencyCollector.cs`、`src/Updating/ComponentDependencyGraph.cs`、`Updater/ShopComponentProtection.cs`；其余既有 Graph/UI 实现保留。
- 本轮独占串行实际复跑：Graph21、PlatformFix30、ScopedFix12、Final WorkerGraph26、UI36、概览28、ShopUi7、Shop82、ShopChecks33、Actions82、Integration29、CrossFlow44 全部通过。证据：`验证/InfrastructureTree20261029/ReleaseFinal/verify_20261029_025014/summary.json` 及逐项日志。旧 Final 失败与旧日志均保留，由 ScopedFix 及本次复跑接续，不修改旧断言以迁就失败。
- 最新 worker 先 publish 到 `Updater/bin/bundle`，完整自包含协议 manifest 校验通过；主插件 `Release --no-restore -p:EnableDeployment=false '-t:Build;CollectReleaseFiles'` 构建0警告0错误，所有验证构建关闭部署。真实CLI使用本次最新bundle；测试仅项目沙盒，不是生产环境操作。
- 固定顶组简中 `基础组件(Core/Patcher)`、繁中 `基礎組件(Core/Patcher)`、英文 `Core/Patcher`；共享普通42行高及既有列坐标。类型标签 `核心组件(Core)`／`核心組件(Core)`／英文 `Core`，`启动补丁(Patcher)`／`啟動補丁(Patcher)`／英文 `Patcher`。名称内为逐层反向消费者树，非自身依赖；灰齿轮、无伪启停、⊘及完整保护原因保留。
- MCE自身及完整传递硬依赖永久禁止独立卸载，保持更新入口及原检查。ScopedFix按node/path保留失败：无关scoped损坏不再污染已知闭包；无种子、无scope失败、MCE闭包失败或完全未解硬依赖仍全局阻断；同path/hash合法共享只扩散文件保护，不串联另一个包私有依赖。未知owner的CanUninstall仅允许进入核查，不授权删除。其他组件安全预览和真实执行仍依赖完整环境、所有权、反向依赖、文件及最终确认校验；不自动确认、不直接删除。
- 交付准备路径：`{relative_zip}`；完整现装备份：`{relative_backup}/`。按当次完整release-files逐项核对（预计23，实际数以部署JSON为准），ZIP CRC与协议完整性检查，不做例行部署hash。
- 实际成功／阻断／回滚、包大小、文件数、两次进程检查及完成时间以 `{relative_run}/deployment_{stamp}.json` 和同名日志为准。本段打包前准备，不预写部署成功；源码／包内／现装README和CHANGELOG须一致。
- 长期授权只更新 `BepInEx/plugins/0核心&基础模组/[界面]ModConfig增强-ModConfigEnhance/`，核对明确缓存无副本冲突。PEAK或MCE.UpdateWorker任一运行即停止，不杀进程、不改任务；完整备份后复制前再确认两者0，失败完整回滚。
- 不写真实core、PEAK_Data、配置、库存、队列或其他模组，不执行真实卸载／更新／迁移／切包，不启动游戏。Unity视觉、滚动／手柄及真实商店交互仍未验收。前文“未部署／待最终负责人”均为历史检查点，由本段及部署JSON接续。
'''
docs=[ROOT/'README.md',ROOT/'CHANGELOG.md',ROOT/'验证/InfrastructureTree20261029/交付清单.md',ROOT.parent/'反编译/反编译索引.md']
for i,path in enumerate(docs):
 dest=ROOT/'备份'/('InfrastructureTree_ReleaseFinal_文档_'+stamp)/f'{i}_{path.name}'
 dest.parent.mkdir(parents=True,exist_ok=True);shutil.copy2(path,dest)
 text=path.read_text(encoding='utf-8')
 if path.name=='交付清单.md':
  text=text.replace('未知安全 | 无真实 MCE 种子、缺失硬引用、坏 DLL／manifest 或采集失败时卸载 fail closed；已知保护路径仍可解释。','未知安全 | 无真实MCE种子、无scope失败、MCE闭包采集失败或完全未解硬引用时全局fail closed；无关scoped坏DLL／manifest保留失败范围，不污染已知MCE链；未知owner仅允许进入核查，不授权删除。')
 path.write_text(text+section,encoding='utf-8')
old=ROOT/'验证/IndependentEnvironment20261028/ReleaseFinal/attempt_delivery_20261028_225517/deploy_20261028_225517.py'
script=old.read_text(encoding='utf-8').replace("STAMP = '20261028_225517'",f"STAMP = '{stamp}'")
script=script.replace("validation=json.loads((HERE.parent/'summary-final.json').read_text(encoding='utf-8'))","validation=json.loads(Path(PLAN['verification']).read_text(encoding='utf-8'))")
(run/f'deploy_{stamp}.py').write_text(script,encoding='utf-8')
(HERE/'delivery-current.json').write_text(json.dumps({'stamp':stamp,'run':str(run),'script':str(run/f'deploy_{stamp}.py'),'plan':plan},ensure_ascii=False,indent=2),encoding='utf-8')
print(str(run/f'deploy_{stamp}.py'))
