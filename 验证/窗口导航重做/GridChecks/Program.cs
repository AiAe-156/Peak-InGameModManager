using ModConfigEnhance.Overview;
using Cell = ModConfigEnhance.Overview.NavigationGrid.Cell;
using Box = ModConfigEnhance.Overview.NavigationGrid.Box;

int checks = 0;
void Equal(int actual, int expected, string name) {
    checks++; if (actual != expected) throw new Exception($"{name}: expected {expected}, got {actual}");
}
Cell Fixed(float x, float y) => new(0, x, y, x, y);
var empty = new Dictionary<int, Box>();
var rows = NavigationGrid.Build(new[] { Fixed(0,100), Fixed(100,100), Fixed(0,50), Fixed(100,50), Fixed(0,0), Fixed(100,0) }, empty);
Equal(rows[0][0], -1, "left edge does not wrap");
Equal(rows[1][1], -1, "right edge does not fall into next row");
Equal(rows[0][1], 1, "right stays in row");
Equal(rows[1][3], 3, "down keeps column");
Equal(rows[3][3], 5, "down continues through rows");
Equal(rows[5][2], 3, "up keeps column");
Equal(rows[5][3], -1, "bottom does not wrap to header");
Equal(rows[0][2], -1, "header does not wrap to footer");

var boxes = new Dictionary<int, Box> { [1] = new(0,-100,100,100), [2] = new(120,-100,220,100) };
Cell In(int area, float x, float y, float sx, float sy) => new(area, x, y, sx, sy);
var cells = new[] {
    Fixed(50,130), Fixed(50,-130),
    In(1,10,-20,10,80), In(1,10,-60,10,40), In(1,10,-180,10,-80),
    In(2,10,-20,130,80), In(2,10,-60,130,40), In(2,10,-180,130,-80)
};
var panes = NavigationGrid.Build(cells, boxes);
Equal(panes[0][3], 2, "header enters first row before footer");
Equal(panes[2][2], 0, "first row returns to header");
Equal(panes[4][3], 1, "last row exits to footer");
Equal(panes[1][2], 4, "footer enters last row");
Equal(panes[2][3], 3, "left pane stays in its own rows");
Equal(panes[5][3], 6, "right pane stays in its own rows");
Equal(panes[3][1], 6, "right enters adjacent pane at visible row");
Equal(panes[6][0], 3, "left returns to adjacent pane at visible row");

// Arbitrary scroll offsets must not change vertical navigation inside a pane.
var moved = cells.Select(c => c.Area == 1 ? In(c.Area,c.X,c.Y,c.ScopeX,c.ScopeY+500) : c).ToArray();
var scrolled = NavigationGrid.Build(moved, boxes);
Equal(scrolled[2][3], 3, "scrolling cannot interleave pane rows");
Equal(scrolled[3][3], 4, "offscreen next row remains reachable");

// Disabled controls are omitted and rebuilt on state transitions.
var enabled = NavigationGrid.Build(new[] { Fixed(0,0), Fixed(50,0), Fixed(100,0) }, empty);
var disabled = NavigationGrid.Build(new[] { Fixed(0,0), Fixed(100,0) }, empty);
Equal(enabled[0][1],1,"enabled action participates");
Equal(disabled[0][1],1,"disabled action is skipped");
Equal(enabled[1][1],2,"reenabled action reconnects");

// A real two-column form: every control can be reached, and every target belongs to this window.
var form = new List<Cell> { Fixed(40,150), Fixed(150,150), Fixed(40,-140), Fixed(150,-140) };
for (int row=0;row<9;row++) {
    form.Add(In(1,20,-row*40,20,80-row*40));
    form.Add(In(2,20,-row*40,140,80-row*40));
    form.Add(In(2,60,-row*40,180,80-row*40));
}
var graph = NavigationGrid.Build(form,boxes);
var seen = new HashSet<int> { 0 }; var queue = new Queue<int>(); queue.Enqueue(0);
while (queue.TryDequeue(out int source)) foreach (int target in graph[source]) {
    checks++; if(target < -1 || target >= form.Count) throw new Exception("foreign navigation target");
    if(target >= 0 && seen.Add(target)) queue.Enqueue(target);
}
Equal(seen.Count,form.Count,"all controls in two-column form are reachable");
Equal(NavigationGrid.Build(Array.Empty<Cell>(),empty).Length,0,"empty window");
// The real help layout: a large reader must be entered from every chapter, at every supported size.
foreach (var (width,height) in new[] { (1888f,1048f), (2528f,1408f), (1248f,688f) }) {
    float side = width < 1000 ? 184 : 210, left = side + 40;
    var helpAreas = new Dictionary<int, Box> { [1] = new(18,-height+34,18+side,-121) };
    var help = new List<Cell> { Fixed(59,-32), Fixed(178,-32) };
    for(int chapter=0;chapter<10;chapter++) help.Add(In(1,side/2,-18.5f-chapter*46,18+side/2,-139.5f-chapter*46));
    int reader = help.Count;
    help.Add(new Cell(0,(left+width-20)/2,-(height+24)/2,(left+width-20)/2,-(height+24)/2,(width-20-left)/2,(height-128)/2));
    var layout = NavigationGrid.Build(help,helpAreas);
    for(int chapter=0;chapter<10;chapter++) Equal(layout[chapter+2][1],reader,$"{width}x{height}: chapter {chapter} reaches reader");
    checks++; if(layout[reader][0]<2) throw new Exception("reader cannot return to chapters");
}

// Crossing uses current screen positions, while the content-row coordinates remain unchanged.
var crossAreas = new Dictionary<int, Box> { [1]=new(0,-100,100,100), [2]=new(120,-100,220,100) };
var crossBefore = new[] { In(1,20,-60,20,40), In(2,20,-60,140,40), In(2,20,-160,140,-60) };
Equal(NavigationGrid.Find(crossBefore,crossAreas,0,1),1,"initial cross-pane entry");
var crossAfter = new[] { crossBefore[0], In(2,20,-60,140,140), In(2,20,-160,140,40) };
Equal(NavigationGrid.Find(crossAfter,crossAreas,0,1),2,"cross-pane movement resamples after independent scrolling");

int gear = ListColumnPolicy.Settings;
int[] normal = {0,1,gear,ListColumnPolicy.Home,ListColumnPolicy.Folder,3};
Equal(ListColumnPolicy.Find(gear,normal,new[]{true,true,false,true,true,true},false),-1,"disabled gear never falls into homepage");
Equal(ListColumnPolicy.Find(gear,new[]{1,1},new[]{true,true},true),-1,"gear navigation skips group fold buttons");
Equal(ListColumnPolicy.Find(1,new[]{1},new[]{true},true),0,"name lane can visit a group");
Equal(ListColumnPolicy.Find(1,normal,new[]{true,true,true,true,true,true},false),1,"name lane resumes after a group");
Equal(ListColumnPolicy.Find(gear,new[]{1,gear,ListColumnPolicy.Home},new[]{true,true,true},false),1,"missing enable cell does not shift gear lane");
Equal(ListColumnPolicy.Find(ListColumnPolicy.Home,normal,new[]{true,true,true,false,true,true},false),-1,"disabled home never falls into folder");
float[] starts = {50,300,343,386,429,472,650,800};
Equal(ListColumnPolicy.FromPosition(20,starts),0,"enable column");
Equal(ListColumnPolicy.FromPosition(150,starts),1,"name column");
Equal(ListColumnPolicy.FromPosition(320,starts),gear,"settings column");
Equal(ListColumnPolicy.FromPosition(365,starts),ListColumnPolicy.Home,"home column");
Equal(ListColumnPolicy.FromPosition(405,starts),ListColumnPolicy.Folder,"folder column");
Equal(ListColumnPolicy.FromPosition(540,starts),3,"version column");

Equal((int)ReadingInput.Step(true,true),-270,"dpad up is 3x");
Equal((int)ReadingInput.Step(false,true),270,"dpad down is 3x");
Equal((int)ReadingInput.Step(true,false),-90,"stick up retains original step");
Equal((int)ReadingInput.Step(false,false),90,"stick down retains original step");
foreach(float available in new[]{1888f,520f,320f}) {
    float[] requested = {160,160,96,80}; var fitted = QueueToolbarSizing.Widths(available,requested,8);
    checks++; if(fitted.Sum()+24>available+.01f) throw new Exception("queue actions overflow their single row");
    checks++; if(fitted.Where((w,i)=>w>requested[i]+.01f).Any()) throw new Exception("queue actions stretched across empty space");
}
Console.WriteLine($"PASS {checks} navigation checks");
