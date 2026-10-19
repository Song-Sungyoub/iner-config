using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Data;
using System.Data;

namespace InerConfig;
public sealed partial class ConfigDocument
{
    public string PasteCsvBlock(int startRow,int startColumn,List<List<string>> values,bool expand,bool headers=false,List<string>? newHeaders=null)
    {
        int height=values.Count,width=values[0].Count;
        if(values.Any(v=>v.Count!=width))throw new FormatException("행별 열 개수가 다릅니다.");
        var edits=new List<(int Start,int End,string Text)>();
        var affected=new Dictionary<int,List<string>>();
        for(int y=0;y<height;y++)
        {
            int i=startRow+y;
            if(!expand&&(i>=Roots.Count||startColumn+width>Roots[i].Children.Count))throw new InvalidOperationException("원본에 없는 셀");
            var fields=i<Roots.Count?Roots[i].Children.Select(c=>Text[c.Start..c.End]).ToList():[];
            while(fields.Count<startColumn+width)fields.Add("");
            for(int x=0;x<width;x++)fields[startColumn+x]=QuoteCsv(values[y][x]);
            affected[i]=fields;
        }
        if(headers&&expand&&Roots.Count>0&&startColumn+width>Roots[0].Children.Count)
        {
            var h=Roots[0].Children.Select(c=>Text[c.Start..c.End]).ToList();int first=h.Count;
            while(h.Count<startColumn+width)
            {
                int index=h.Count-first;if(newHeaders==null||index>=newHeaders.Count||string.IsNullOrWhiteSpace(newHeaders[index]))throw new InvalidOperationException("새 열 제목을 지정하세요.");
                h.Add(QuoteCsv(newHeaders[index]));
            }
            affected[0]=h;
        }
        foreach(var (i,fields) in affected.Where(k=>k.Key<Roots.Count))
            edits.Add((Roots[i].Start,Roots[i].Children.Last().End,string.Join(Separator,fields)));
        var result=Text;foreach(var e in edits.OrderByDescending(e=>e.Start))result=result[..e.Start]+e.Text+result[e.End..];
        string nl=Text.Contains("\r\n")?"\r\n":"\n";
        for(int i=Roots.Count;i<startRow+height;i++)
        {
            if(result.Length>0&&!result.EndsWith('\n')&&!result.EndsWith('\r'))result+=nl;
            result+=string.Join(Separator,affected.TryGetValue(i,out var cells)?cells:Enumerable.Repeat("",startColumn+width))+nl;
        }
        return Checked(result);
    }
    // All positions refer to the original document; apply right to left and validate once.
    public string ChangeCsvCells(IEnumerable<(int Row,int Column,string Value)> changes)
    {
        if(Format!="CSV")throw new InvalidOperationException();
        var edits=new List<(int Start,int End,string Value)>();
        foreach(var (row,col,value) in changes)
        {
            if(row<0||row>=Roots.Count||col<0||col>=Roots[row].Children.Count)
                throw new InvalidOperationException("원본에 없는 셀입니다. 구조 편집에서 행·열을 먼저 추가하세요.");
            var n=Roots[row].Children[col];edits.Add((n.Start,n.End,QuoteCsv(value)));
        }
        if(edits.Select(x=>(x.Start,x.End)).Distinct().Count()!=edits.Count)throw new InvalidOperationException("중복 셀 변경");
        var result=Text;foreach(var e in edits.OrderByDescending(x=>x.Start))result=result[..e.Start]+e.Value+result[e.End..];
        return Checked(result);
    }
}
public partial class MainWindow
{
    private async void RenameCsvHeader(ConfigNode node)
    {
        if(!RequireStructure()||document==null||Session?.CsvHeader!=true||node.Parent==null)return;
        int col=node.Parent.Children.IndexOf(node);
        if(col>=document.Roots[0].Children.Count)return;
        var header=document.Roots[0].Children[col];
        var d=new EditDialog(this,"헤더 이름 변경","열 제목",header.Value,"String");
        if(d.ShowDialog()!=true)return;
        try{await Apply(document.Change(header,d.Value));}catch(Exception ex){Error(ex);}
    }
    private string? csvNextPath;
    private bool csvContinue, csvComposing;
    private void InitializeCsvEditing()
    {
        FileSession? editSession=null;ConfigDocument? editDocument=null;int editRow=-1,editCol=-1;
        Csv.SelectionMode=DataGridSelectionMode.Extended;
        Csv.IsReadOnly=false;Csv.HeadersVisibility=DataGridHeadersVisibility.All;Csv.RowHeaderWidth=25;
        Csv.LoadingRow+=(_,e)=>{int i=e.Row.GetIndex();if(i>=0&&i<csvRows.Count){e.Row.Header=new TextBlock{Text="⠿",ToolTip="구조 편집: 행 순서 드래그"};AttachMove(e.Row,csvRows[i]);}};
        Csv.CanUserReorderColumns=false;Csv.CanUserDeleteRows=false;
        Csv.ClipboardCopyMode=DataGridClipboardCopyMode.ExcludeHeader;
        Csv.PreparingCellForEdit+=(_,e)=>{if(e.EditingElement is TextBox box){box.AcceptsReturn=!inlineEnabled;
            TextCompositionManager.AddPreviewTextInputStartHandler(box,(_,_)=>csvComposing=true);
            TextCompositionManager.AddPreviewTextInputHandler(box,(_,_)=>Dispatcher.BeginInvoke(()=>csvComposing=false));
            box.PreviewKeyDown+=(_,key)=>{
                if(!inlineEnabled||csvComposing||key.Key==Key.ImeProcessed)return;
                if(key.Key is not (Key.Enter or Key.Tab or Key.Up or Key.Down))return;
                if(Keyboard.Modifiers is not (ModifierKeys.None or ModifierKeys.Shift))return;
                int row=Csv.Items.IndexOf(Csv.CurrentCell.Item),col=Csv.CurrentCell.Column?.DisplayIndex??-1;
                if((key.Key is Key.Up or Key.Down)&&Keyboard.Modifiers!=ModifierKeys.None)return;
                int delta=key.Key==Key.Up?-1:key.Key==Key.Down?1:Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)?-1:1;
                if(key.Key is Key.Enter or Key.Up or Key.Down)row+=delta;
                else {col+=delta;if(col>=Csv.Columns.Count){row++;col=0;}else if(col<0){row--;col=Csv.Columns.Count-1;}}
                csvNextPath=row>=0&&row<csvRows.Count&&col>=0&&col<csvRows[row].Children.Count?csvRows[row].Children[col].Path:null;
                csvContinue=true;key.Handled=true;Csv.CommitEdit(DataGridEditingUnit.Cell,true);
            };
            box.TextChanged+=(_,_)=>{CaptureCellInput();};box.PreviewKeyDown+=(_,key)=>{if(key.Key==Key.Enter&&Keyboard.Modifiers==ModifierKeys.Alt){box.SelectedText="\\n";box.CaretIndex+=1;key.Handled=true;}};}};
        Csv.BeginningEdit+=(_,e)=>
        {
            int row=Csv.Items.IndexOf(e.Row.Item),col=e.Column.DisplayIndex;
            if(inlineEnabled&&row>=0&&row<csvRows.Count&&col<csvRows[row].Children.Count&&csvRows[row].Children[col].Value.IndexOfAny(['\r','\n'])>=0)
            {e.Cancel=true;var target=csvRows[row].Children[col];Dispatcher.BeginInvoke(()=>Edit(target,false,true));return;}
            editSession=Session;editDocument=document;editRow=row>=0&&row<csvRows.Count?document?.Roots.IndexOf(csvRows[row])??-1:-1;editCol=col;
            if(row<0||row>=csvRows.Count||csvRows[row].PendingDelete||col>=csvRows[row].Children.Count)
            {e.Cancel=true;Message.Text="원본에 없는 셀이거나 삭제 예정 행입니다. 구조 편집에서 확장하거나 삭제를 복구하세요.";}
        };
        Csv.CellEditEnding+=(_,e)=>
        {
            if(e.EditAction==DataGridEditAction.Cancel&&Session!=null){Session.Input=null;Session.Notify();}
            if(e.EditAction!=DataGridEditAction.Commit||e.EditingElement is not TextBox box||document==null)return;
            int col=editCol;
            if(editSession==null||editDocument==null||editRow<0)return;
            var original=editDocument;var session=editSession;
            int committedRow=editRow;var targetPath=csvNextPath;bool resume=csvContinue;csvNextPath=null;csvContinue=false;
            if(session.Text!=original.Text){e.Cancel=true;Message.Text="편집 대상 문서가 변경되었습니다.";return;}
            try
            {
                string changed=original.ChangeCsvCells([(editRow,col,box.Text)]);
                if(session!=null){session.Input=null;session.Change(changed);}
                if(Session==session){switching=true;Source.Text=changed;switching=false;}
                Dispatcher.BeginInvoke(async ()=>
                {
                    if(Session!=session)return;
                    int nextRow=Csv.Items.IndexOf(Csv.CurrentCell.Item),nextColumn=Csv.CurrentCell.Column?.DisplayIndex??col;
                    await Parse();if(Session!=session)return;Csv.UpdateLayout();
                    if(resume)
                    {
                        var target=csvRows.SelectMany(r=>r.Children).FirstOrDefault(n=>n.Path==targetPath);
                        if(target!=null){nextRow=csvRows.IndexOf(target.Parent!);nextColumn=target.Parent!.Children.IndexOf(target);}
                        else {nextRow=committedRow-(session!.CsvHeader?1:0);nextColumn=col;}
                    }
                    if(nextRow>=0&&nextRow<Csv.Items.Count&&nextColumn>=0&&nextColumn<Csv.Columns.Count)
                    {Csv.CurrentCell=new DataGridCellInfo(Csv.Items[nextRow],Csv.Columns[nextColumn]);Csv.ScrollIntoView(Csv.Items[nextRow],Csv.Columns[nextColumn]);if(resume&&targetPath!=null&&csvRows[nextRow].Children[nextColumn].Value.IndexOfAny(['\r','\n'])<0){Csv.UpdateLayout();Csv.BeginEdit();}}
                });
            }
            catch(Exception ex){e.Cancel=true;Error(ex);}
        };
        Csv.CommandBindings.Add(new CommandBinding(ApplicationCommands.Copy,(_,e)=>{if(FindFocusedCsv()!=null)return;CopyCsv(false);e.Handled=true;}));
        Csv.CommandBindings.Add(new CommandBinding(ApplicationCommands.Paste,async(_,e)=>{if(FindFocusedCsv()!=null)return;await PasteCsv(false);e.Handled=true;}));
        Csv.PreviewKeyDown+=async(_,e)=>
        {
            if(Find<TextBox>(e.OriginalSource as DependencyObject)!=null)return;
            if(e.Key==Key.Delete){e.Handled=true;await PasteCsv(true);}
        };
    }
    private (int Top,int Left,int Bottom,int Right)? CsvRange()
    {
        var cells=Csv.SelectedCells.Where(c=>c.IsValid).Select(c=>(Row:Csv.Items.IndexOf(c.Item),Col:c.Column.DisplayIndex)).Where(c=>c.Row>=0).ToArray();
        if(cells.Length==0)return null;
        int top=cells.Min(c=>c.Row),left=cells.Min(c=>c.Col),bottom=cells.Max(c=>c.Row),right=cells.Max(c=>c.Col);
        if(cells.Length!=(bottom-top+1)*(right-left+1))throw new InvalidOperationException("연속된 직사각형 범위를 선택하세요.");
        return(top,left,bottom,right);
    }
    private void CopyCsv(bool headers)
    {
        try
        {
            var range=CsvRange();if(range==null)return;var q=range.Value;
            static string Quote(string s)=>s.IndexOfAny(['\t','\r','\n','"'])>=0?"\""+s.Replace("\"","\"\"")+"\"":s;
            var rows=new List<string>();
            if(headers)rows.Add(string.Join("\t",Enumerable.Range(q.Left,q.Right-q.Left+1).Select(i=>Quote(Csv.Columns[i].Header?.ToString()??""))));
            for(int row=q.Top;row<=q.Bottom;row++)
                rows.Add(string.Join("\t",Enumerable.Range(q.Left,q.Right-q.Left+1).Select(i=>Quote(i<csvRows[row].Children.Count?csvRows[row].Children[i].Value:""))));
            Clipboard.SetText(string.Join("\r\n",rows));
        }
        catch(Exception ex){Error(ex);}
    }
    private async Task PasteCsv(bool clear)
    {
        if(document==null||Session==null)return;
        try
        {
            if(Search.Text.Length>0)throw new InvalidOperationException("검색을 해제한 뒤 범위를 수정하세요.");
            var range=CsvRange();if(range==null)return;var q=range.Value;
            if(Session.PendingDeletes.Count>0)throw new InvalidOperationException("삭제 예정 작업을 완료하거나 복구한 뒤 범위를 수정하세요.");
            var values=clear?new List<List<string>>{new(){""}}:new ConfigDocument(Clipboard.GetText(),"CSV",'\t').Roots.Select(r=>r.Children.Select(c=>c.Value).ToList()).ToList();
            if(values.Count==0)values.Add([""]);
            int height=values.Count,width=values.Max(x=>x.Count);
            if(values.Any(x=>x.Count!=width))throw new InvalidOperationException("붙여넣을 행의 열 개수가 다릅니다.");
            bool fill=height==1&&width==1;
            if(!fill&&(q.Top!=q.Bottom||q.Left!=q.Right)&&(height!=q.Bottom-q.Top+1||width!=q.Right-q.Left+1))
                throw new InvalidOperationException("선택 범위와 붙여넣기 크기가 다릅니다.");
            if(fill){height=q.Bottom-q.Top+1;width=q.Right-q.Left+1;}
            if(fill)values=Enumerable.Range(0,height).Select(_=>Enumerable.Repeat(values[0][0],width).ToList()).ToList();
            int rowOffset=Session.CsvHeader?1:0,startRow=q.Top+rowOffset;
            bool expands=startRow+height>document.Roots.Count||Enumerable.Range(startRow,height).Any(i=>i>=document.Roots.Count||q.Left+width>document.Roots[i].Children.Count);
            var names=new List<string>();
            if(expands)
            {
                if(!RequireStructure())throw new InvalidOperationException("표 범위를 넘거나 없는 셀입니다. 구조 편집에서 붙여넣으세요.");
                if(MessageBox.Show(this,$"붙여넣기에 필요한 행·셀을 확장합니다.\n범위: {height}행 × {width}열","CSV 확장",MessageBoxButton.OKCancel)!=MessageBoxResult.OK)return;
                if(Session.CsvHeader&&document.Roots.Count>0)
                    for(int i=document.Roots[0].Children.Count;i<q.Left+width;i++)
                    {var d=new EditDialog(this,"열 제목", $"{i+1}번째 열의 제목", "", "String");if(d.ShowDialog()!=true)return;names.Add(d.Value);}
            }
            var text=document.PasteCsvBlock(startRow,q.Left,values,expands,Session.CsvHeader,names);await Apply(text);Message.Text=$"{height*width}개 셀 변경 · Ctrl+Z로 실행 취소";
        }
        catch(Exception ex){Error(ex);}
    }
}

internal sealed class CsvDisplayValue : System.Windows.Data.IValueConverter
{
    public object Convert(object value,Type target,object parameter,System.Globalization.CultureInfo culture)=>value==DBNull.Value?"—":value;
    public object ConvertBack(object value,Type target,object parameter,System.Globalization.CultureInfo culture)=>value;
}
