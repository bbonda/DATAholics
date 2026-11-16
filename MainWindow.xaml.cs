using System.Windows;
using System.Windows.Controls;
using System.Text.Json;
using System.IO;
namespace AccurateHireIQ;
public partial class MainWindow : Window
{
 readonly MainViewModel vm;
 public MainWindow(MainViewModel vm){this.vm=vm;InitializeComponent();DataContext=vm;Loaded+=async(_,_)=>{if(vm.Store.ActiveBatch==0)await vm.LoadSample();};}
 private void Upload_DragOver(object sender,DragEventArgs e){e.Effects=e.Data.GetDataPresent(DataFormats.FileDrop)?DragDropEffects.Copy:DragDropEffects.None;e.Handled=true;}
 private async void Upload_Drop(object sender,DragEventArgs e){if(e.Data.GetData(DataFormats.FileDrop) is string[] files&&files.Length>0)await vm.PreviewFile(files[0]);}
 private async void Sheet_Changed(object sender,SelectionChangedEventArgs e){if(IsLoaded&&!vm.Busy)await vm.ChangeSheet();}
 private void Key_Changed(object sender,RoutedEventArgs e){if(vm!=null)vm.ApiKey=((PasswordBox)sender).Password;}
 private async void CategoryExport_Click(object sender,RoutedEventArgs e){vm.SetFilter(new(Status:((Button)sender).Tag?.ToString()??""));await vm.ExportCurrent();}
 private async void AnalysisExport_Click(object sender,RoutedEventArgs e)=>await vm.ExportCurrent(true);
 private async void Demo_Click(object sender,RoutedEventArgs e){var d=new Microsoft.Win32.SaveFileDialog{Filter="Excel workbook|*.xlsx",FileName="AccurateHire_Synthetic_Demo_10000.xlsx"};if(d.ShowDialog()!=true)return;await vm.Work(async ct=>{vm.Activity="Generating 10,000 fictional demo records…";await Task.Run(()=>DemoData.Generate(d.FileName),ct);vm.Activity="Synthetic demo saved. Import it to see 7,500 PASS / 2,000 REVIEW / 500 STOP.";});}
 private void Analyze_Click(object sender,RoutedEventArgs e){vm.Navigate("AI Agent Chat");vm.Prompt="Analyze the uploaded screening data and identify incomplete verification records. Cite source rows and distinguish missing overall decisions from individual findings.";}
 private void Suggestion_Click(object sender,RoutedEventArgs e){vm.Prompt=((Button)sender).Content.ToString()??"";}
 private void Details_Click(object sender,RoutedEventArgs e)
 {
  if(vm.SelectedCandidate is not {} row)return;var values=JsonSerializer.Deserialize<Dictionary<string,string>>(row.OriginalJson)!;
  var panel=new StackPanel{Margin=new Thickness(25)};panel.Children.Add(new TextBlock{Text="Candidate source record",FontSize=24,FontWeight=FontWeights.SemiBold});panel.Children.Add(new TextBlock{Text=$"Batch {vm.Store.ActiveBatch} · {row.File} · {row.Sheet} · Excel row {row.SourceRow}\nScreening classification: {row.StatusLabel}",Margin=new Thickness(0,12,0,18),TextWrapping=TextWrapping.Wrap});
  var classification=vm.Store.ClassificationFor(row.RowId);if(classification!=null){panel.Children.Add(new TextBlock{Text=$"Classification basis: {classification.Basis}\n{classification.Reason}",TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,0,0,12),FontWeight=FontWeights.SemiBold});foreach(var evidence in classification.Evidence)panel.Children.Add(new TextBlock{Text=$"{evidence.Column}: {evidence.Value}",TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,0,0,5)});}
  var fields=values.Select(k=>new{Column=k.Key,SourceValue=vm.Store.ReadSettings().MaskPersonal&&new[]{"name","email","phone","mobile","address","gender","ssn","pan"}.Any(s=>k.Key.Contains(s,StringComparison.OrdinalIgnoreCase))&&!ImportService.IsCheck(k.Key)?"[masked — disable masking in Settings to reveal]":k.Value}).ToList();
  var grid=new DataGrid{AutoGenerateColumns=true,ItemsSource=fields,Height=350,IsReadOnly=true};panel.Children.Add(grid);panel.Children.Add(new TextBlock{Text="Calculated operational findings · not an eligibility assessment",FontWeight=FontWeights.SemiBold,Margin=new Thickness(0,24,0,12)});
  var findings=vm.Store.Findings(row:row.RowId);foreach(var f in findings)panel.Children.Add(new TextBlock{Text=$"{f.Type} | {f.Column}: {f.Value}\n{f.Explanation}\nFollow-up: {f.FollowUp}",TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,0,0,14)});
  if(findings.Count==0)panel.Children.Add(new TextBlock{Text="No configured data-quality rule matched. This does not establish verification completeness or eligibility."});
  var ask=new Button{Content="Ask agent about this source row",HorizontalAlignment=HorizontalAlignment.Left};var w=new Window{Title="Source details · AccurateHire IQ",Width=960,Height=800,Owner=this,WindowStartupLocation=WindowStartupLocation.CenterOwner,Content=new ScrollViewer{Content=panel,VerticalScrollBarVisibility=ScrollBarVisibility.Auto}};ask.Click+=(_,_)=>{w.Close();vm.Navigate("AI Agent Chat");vm.Prompt=$"Explain the original screening information for source row {row.SourceRow}. Cite evidence and do not infer a hiring decision.";};panel.Children.Add(ask);w.ShowDialog();
 }
}
