from pathlib import Path
import sys
root = Path(sys.argv[1])
p = root / 'src/Overview/OverviewPage.Installation.cs'
s = p.read_text(encoding='utf-8')
start = s.index('            _installationSeen = task.Id; _installationExpected = "";')
end = s.index('        catch (Exception error) { SetStatus(Loc.LocalizeMessage(error.Message)); }\n    }\n\n    /// <summary>Closing the review dialog')
new = '''            _installationSeen = task.Id; _installationExpected = "";
            // A plan with blockers can never be confirmed; close it so it does not sit "ready", then show why.
            if (task.Blockers.Count > 0) CancelInstallationPreview(task);
            ShowInstallationReview(task);
        }
'''
s = s[:start] + new + s[end:]
marker = '''    /// <summary>Closing the review dialog without confirming used to leave the preview "ready" in the worker, which the
    /// queue shows as "preparing files" forever with no way to dismiss it (and which kept the helper starting).</summary>'''
assert marker in s
s = s.replace(marker, (root / '验证/2.2.12整理入口与更新记录/review_block.cs.txt').read_text(encoding='utf-8') + marker)
if 'using UnityEngine.UI;' not in s:
    s = s.replace('using UnityEngine;\n', 'using UnityEngine;\nusing UnityEngine.UI;\n')
p.write_text(s, encoding='utf-8')
print('ok')
