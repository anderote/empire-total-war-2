#!/usr/bin/env python3
"""Build a local, self-contained index of the Unity animation review captures."""
import html
import json
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
OUTPUT = ROOT / "Artifacts/AnimationQuality"


def main():
    entries = []
    for path in sorted((OUTPUT / "After").glob("*.png")):
        name = path.stem
        stage = "Artillery" if name.startswith("Artillery") else "Cavalry" if "cavalry" in name else "Melee" if "melee" in name else "Infantry & command"
        entries.append({"name": name, "stage": stage, "faction": name.split("-")[0],
                        "after": "After/" + path.name,
                        "before": "Before/" + path.name if (OUTPUT / "Before" / path.name).is_file() else None})
    checks = (OUTPUT / "verification.txt").read_text() if (OUTPUT / "verification.txt").is_file() else "Validation is still in progress."
    meshes = (OUTPUT / "mesh-audit.txt").read_text() if (OUTPUT / "mesh-audit.txt").is_file() else ""
    template = r'''<!doctype html><html lang="en"><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1">
<title>ETW2 · Animation quality review</title><style>
:root{color-scheme:dark;font-family:system-ui,sans-serif;background:#10151b;color:#e9eef3}*{box-sizing:border-box}body{margin:0}header{padding:28px 32px 20px;border-bottom:1px solid #34404e}h1{font-size:27px;margin:0 0 8px}p{line-height:1.55;max-width:1050px;color:#b7c6d6}nav{display:flex;gap:8px;flex-wrap:wrap;margin-top:22px}button,input,select{font:inherit;color:inherit;background:#202a35;border:1px solid #445467;border-radius:6px;padding:10px 14px}button{cursor:pointer}button[aria-pressed=true]{background:#31586a;border-color:#76aec3}.layout{display:grid;grid-template-columns:280px minmax(0,1fr);gap:24px;padding:24px 32px}aside{display:flex;flex-direction:column;gap:8px;max-height:74vh;overflow:auto}aside button{text-align:left;font-size:13px;padding:9px 12px}main{min-width:0}h2{font-size:22px;margin-top:4px}.tools{display:flex;gap:10px;align-items:center;flex-wrap:wrap;margin-bottom:18px}.sheet{width:100%;border:1px solid #364455;border-radius:8px;display:block}.caption{font-size:13px;color:#9aafc4}.status{font-size:13px;background:#1a242c;padding:14px;border-radius:7px;line-height:1.55;white-space:pre-wrap}.note{border-left:3px solid #80bbcf;padding-left:15px;margin:18px 0 22px}a{color:#8dc6df}label{font-size:13px}.empty{padding:30px;color:#b7c6d6}@media(max-width:760px){.layout{grid-template-columns:1fr;padding:18px}aside{max-height:180px}header{padding:22px}h1{font-size:23px}}
</style><header><h1>Animation quality review</h1><p>Production poses, rendered from the front and side at six points in each action. Work through line infantry and command roles, artillery, cavalry, then melee.</p>
<nav id="stages"></nav></header><div class="layout"><aside><label for="search">Find an action or role</label><input id="search" placeholder="reload, sergeant, gallop…"><label for="faction">Faction</label><select id="faction"><option>All</option><option>French</option><option>British</option></select><div id="tracks"></div></aside>
<main><h2 id="title"></h2><p class="note" id="note"></p><div class="tools"><button id="previous">← Previous</button><button id="next">Next →</button><button id="version" aria-pressed="false">Show before</button><a id="full" target="_blank">Open full resolution</a></div>
<img class="sheet" id="sheet" alt="Animation frame sequence"><p class="caption">Left to right: 0%, 20%, 40%, 60%, 80%, 100%. Infantry and cavalry: front view above, side view below. Artillery: full crew and gun. Loop endpoints return to the start pose.</p>
<p class="caption">These frame sequences show pose shape and contacts. Battle cadence and event timing are checked separately. Existing firing, reloading, kneeling, death and cavalry meshes are included for comparison; the rebaked changes cover infantry locomotion, marching command roles and retreat.</p>
<details><summary>Checks and remaining visual limitations</summary><p class="status">__CHECKS__</p><p class="status">__MESHES__</p><p>Cavalry still uses the current simplified horse model. Final art acceptance also needs full-speed review of action transitions in formations. This pass checks the common French/British roster; regional retargets retain their current pose assets.</p><p><a href="foot-contact.txt">Foot-contact measurements</a> · <a href="mesh-audit.txt">Mesh audit</a> · <a href="verification.txt">Validation receipt</a></p></details>
</main></div><script>
const entries=__DATA__;
const stages=['Infantry & command','Artillery','Cavalry','Melee'];
const notes={
'Infantry & command':'Check the shoulder and cheek contact when firing, both hands and ramrod during reload, weight over the front foot when kneeling, planted feet during movement, and the different command-role equipment. Foot cadence, leg reach and command-role timing have been corrected.',
'Artillery':'Follow the crew through approach, sponging, loading, ramming, running the gun forward and aiming. Crew members face their travel direction, turn toward the gun, ease into service poses and handle one visible round during loading.',
'Cavalry':'Inspect walk, trot, gallop, turns, strikes and separate horse/rider falls. Stationary breathing now advances; moving riders interpolate between simulation steps. Horse geometry remains simplified.',
'Melee':'Inspect preparation, contact and recovery for thrusts, parries, stock strikes and retreat. The retreat sequence now retains its backward footwork. Strike contact timing has a separate automated check.'};
let stage=stages[0],selected=null,before=false,visible=[];
const el=id=>document.getElementById(id);
const pretty=n=>n.replace(/^(French|British|Artillery)-\d+-/,'$1 · ').replace(/command-0/,'captain').replace(/command-1/,'drummer').replace(/command-2/,'standard bearer').replace(/command-3/,'pike sergeant').replaceAll('-',' ');
function render(){
 el('stages').replaceChildren(...stages.map(s=>{const b=document.createElement('button');b.textContent=(stages.indexOf(s)+1)+'. '+s;b.setAttribute('aria-pressed',s===stage);b.onclick=()=>{stage=s;selected=null;before=false;render()};return b}));
 const term=el('search').value.toLowerCase(),faction=el('faction').value;
 visible=entries.filter(e=>e.stage===stage&&(faction==='All'||e.faction===faction||e.faction==='Artillery')&&pretty(e.name).toLowerCase().includes(term));
 if(!visible.includes(selected))selected=visible[0];
 el('tracks').replaceChildren(...visible.map(e=>{const b=document.createElement('button');b.style.cssText='width:100%;margin-bottom:5px';b.textContent=pretty(e.name);b.setAttribute('aria-pressed',e===selected);b.onclick=()=>{selected=e;before=false;render()};return b}));
 el('note').textContent=notes[stage];el('sheet').hidden=!selected;
 el('title').textContent=selected?pretty(selected.name):'No matching actions';
 el('version').disabled=!selected?.before;el('version').textContent=before?'Show after':'Show before';el('version').setAttribute('aria-pressed',before);
 if(selected){const url=before&&selected.before?selected.before:selected.after;el('sheet').src=url;el('full').href=url;el('sheet').alt=pretty(selected.name)+' '+(before?'before':'after')+' frame sequence'}
 el('previous').disabled=!selected||visible.indexOf(selected)===0;el('next').disabled=!selected||visible.indexOf(selected)===visible.length-1;
}
el('version').onclick=()=>{before=!before;render()};el('search').oninput=render;el('faction').onchange=render;
el('previous').onclick=()=>{selected=visible[Math.max(0,visible.indexOf(selected)-1)];before=false;render()};el('next').onclick=()=>{selected=visible[Math.min(visible.length-1,visible.indexOf(selected)+1)];before=false;render()};render();
</script></html>'''
    page = template.replace("__CHECKS__", html.escape(checks)).replace("__MESHES__", html.escape(meshes)).replace("__DATA__", json.dumps(entries).replace("<", "\\u003c"))
    (OUTPUT / "review.html").write_text(page)
    print(f"Review page: {OUTPUT / 'review.html'} ({len(entries)} sequences)")


if __name__ == "__main__":
    main()
