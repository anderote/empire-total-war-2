"""Compose rendered outfit and motion previews into review sheets."""
from pathlib import Path
import argparse, math
from PIL import Image, ImageDraw
parser=argparse.ArgumentParser();parser.add_argument('folder',type=Path);args=parser.parse_args()
folder=args.folder
images=sorted(p for p in folder.glob('*.png') if not p.name.endswith('-sheet.png'))
def sheet(name,paths,columns=5):
    if not paths:return
    width,height=360,390
    canvas=Image.new('RGB',(width*columns,height*math.ceil(len(paths)/columns)),(35,39,45));draw=ImageDraw.Draw(canvas)
    for index,path in enumerate(paths):
        tile=Image.open(path).convert('RGB');tile.thumbnail((width,height-30))
        x=(index%columns)*width+(width-tile.width)//2;y=(index//columns)*height
        canvas.paste(tile,(x,y));draw.text(((index%columns)*width+8,y+height-24),path.stem,fill=(235,235,235))
    canvas.save(folder/(name+'-sheet.png'))
mounted=lambda p:p.stem.endswith(('cuirassier','dragoon','hussar','lancer'))
for faction in ['france','britain','prussia','russia','austria','hungary','italy','ottoman']:
    sheet(faction,[p for p in images if p.stem.startswith(faction+'-') and not mounted(p)],4)
sheet('cavalry',[p for p in images if mounted(p)],5)
sheet('commands',[p for p in images if p.stem.startswith('command-')],4)
for prefix in ['grenade','SlashRight','LanceReady','LanceThrust','MountedAim','MountedFire','MountedReload','PistolAim','PistolFire','PistolReload']:
    sheet(prefix,sorted([p for p in images if p.stem.startswith(prefix+'-')],key=lambda p:int(p.stem.rsplit("-",1)[1])),4)
print('Composed review sheets for',len(images),'rendered poses')
