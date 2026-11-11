using ModConfigEnhance.Overview;
int n=0;
void Check(bool b,string why){if(!b)throw new Exception(why);Console.WriteLine("PASS "+ ++n+" "+why);}
Check(DependencyDepthStyle.Ink(0)=="#EEE8CE","direct relationships retain bright ivory");
Check(DependencyDepthStyle.Ink(-1)==DependencyDepthStyle.Ink(0),"negative depth clamped");
double Light(string hex)=>.2126*Convert.ToInt32(hex.Substring(1,2),16)+.7152*Convert.ToInt32(hex.Substring(3,2),16)+.0722*Convert.ToInt32(hex.Substring(5,2),16);
for(int depth=1;depth<=32;depth++){
 Check(Light(DependencyDepthStyle.Ink(depth))<Light(DependencyDepthStyle.Ink(depth-1)),"each child level is weaker "+depth);
 Check(Light(DependencyDepthStyle.Ink(depth))>132,"deep level readable floor "+depth);
}
Check(DependencyDepthStyle.Wrap("name <color=#EFAB80>warning</color>",2).Contains("<color=#EFAB80>warning</color>"),"nested warning color preserved");
Check(DependencyDepthStyle.Ink(1)!=DependencyDepthStyle.Ink(0),"second level visually distinct");
Console.WriteLine("RESULT "+n+" production palette checks passed");
