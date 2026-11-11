from pathlib import Path
import json, zipfile
ROOT=Path(__file__).resolve().parents[3];HERE=Path(__file__).parent
current=json.loads((HERE/'delivery-current.json').read_text(encoding='utf-8'));stamp=current['stamp'];run=Path(current['run'])
r=json.loads((run/f'deployment_{stamp}.json').read_text(encoding='utf-8'))
assert r['status']=='success' and r['files']==23 and all(not c['processes'] for c in r['process_checks'])
with zipfile.ZipFile(r['zip']) as z:
 assert z.testzip() is None
 for name in ['README.md','CHANGELOG.md']:
  assert z.read(name)==(ROOT/name).read_bytes()==(Path(r['target'])/name).read_bytes()
text=f'''\n\n## ReleaseFinal 实际部署结果（{stamp}）

- **成功**；完成时间 `{r['completed_at']}`，版本2.0.0。最新worker publish／协议完整性、主插件Release Build;CollectReleaseFiles及全部12组指定实run通过；构建0警告0错误。全部旧日志保留，生产及断言未改。
- ZIP：`{Path(r['zip']).relative_to(ROOT).as_posix()}`；**{r['zip_size']:,} bytes**；CRC通过；完整release-files **23文件**已全部复制并逐项核对存在／大小，源码／ZIP／现装README、CHANGELOG字节一致。
- 完整旧安装备份：`{Path(r['backup']).relative_to(ROOT).as_posix()}/`，{r['backup_files']}文件；独立脚本保留完整失败回滚机制，本次未触发回滚。
- 开始打包备份前 `{r['process_checks'][0]['time']}` 及复制前 `{r['process_checks'][1]['time']}`：PEAK与MCE.UpdateWorker均0。既定启用目录存在、明确缓存候选均不存在，无副本冲突；未杀进程／修改任务。
- 唯一部署目标：`{r['target']}`。未操作真实core／PEAK_Data／配置／库存／队列／其他模组，未真实卸载／更新／迁移／切包，未启动游戏。Unity视觉及真实交互未验收。
- 证据：`验证/InfrastructureTree20261029/ReleaseFinal/verify_20261029_025014/summary.json`；`{run.relative_to(ROOT).as_posix()}/deployment_{stamp}.json` 与同名日志。README／CHANGELOG打包前已准备，此后不追写以保持包内／现装一致；本结果仅追加清单、分区索引及本目录记录。
'''
(run/'部署结果.md').write_text('# 基础组件依赖树最终授权部署\n'+text,encoding='utf-8')
for path in [ROOT/'验证/InfrastructureTree20261029/交付清单.md',ROOT.parent/'反编译/反编译索引.md']:
 with path.open('a',encoding='utf-8') as f:f.write(text)
(HERE/'最终结果.md').write_text('# 最终复跑与部署结果\n'+text,encoding='utf-8')
print(text)
