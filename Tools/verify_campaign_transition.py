#!/usr/bin/env python3
"""Run actual campaign scene transitions in Unity play mode, with isolated result logs."""
import subprocess
from pathlib import Path
root=Path(__file__).resolve().parent.parent
unity='/Applications/Unity/Hub/Editor/6000.6.3f1/Unity.app/Contents/MacOS/Unity'
base=[unity,'-batchmode','-nographics','-projectPath',str(root)]
commands=[base+['-quit','-executeMethod','CampaignBattleIntegrationVerification.Verify','-logFile',str(root/'Artifacts/CampaignBattleIntegration/editor-verification.log')]]
for mode in ['attack','defend','cancel']:
    commands.append(base+['-executeMethod','CampaignBattleIntegrationVerification.VerifyTransition','--campaign-battle-probe',mode,
        '--campaign-probe-output',str(root/'Artifacts/CampaignBattleIntegration'/('editor-transition-'+mode+'.json')),
        '-logFile',str(root/'Artifacts/CampaignBattleIntegration'/('editor-transition-'+mode+'.log'))])
for command in commands:
    print('RUN',command[command.index('-executeMethod')+1],command[-1],flush=True)
    result=subprocess.run(command,timeout=180)
    print('EXIT',result.returncode,flush=True)
    if result.returncode:raise SystemExit(result.returncode)
