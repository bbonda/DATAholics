using System.Net;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
namespace AccurateHireIQ;
// A local protocol fixture exercises the actual Agent Framework tool loop, not model reasoning.
public static class AgentIntegrationTest
{
 public static async Task Run(Store store,AgentService agent)
 {
  var socket=new TcpListener(IPAddress.Loopback,0);socket.Start();int port=((IPEndPoint)socket.LocalEndpoint).Port;socket.Stop();
  using var listener=new HttpListener();listener.Prefixes.Add($"http://localhost:{port}/");listener.Start();using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(35));var previous=store.ReadSettings();
  var local=store.ReadSettings();local.Endpoint=$"http://localhost:{port}/v1";local.Model="protocol-test";local.ProtectedKey="";local.AllowExternal=false;store.SaveSettings(local);agent.Reset();
  try{
   var server=Task.Run(async()=>{
    for(int turn=0;turn<2;turn++){
     var context=await listener.GetContextAsync().WaitAsync(timeout.Token);using var reader=new StreamReader(context.Request.InputStream);string body=await reader.ReadToEndAsync(timeout.Token);using var json=JsonDocument.Parse(body);var root=json.RootElement;
     object message;
     if(turn==0){if(!root.GetProperty("tools").EnumerateArray().Any(t=>t.GetProperty("function").GetProperty("name").GetString()=="GenerateOperationalSummary"))throw new Exception("Agent tools were not sent to provider");message=new{role="assistant",content=(string?)null,tool_calls=new[]{new{id="call_test",type="function",function=new{name="GenerateOperationalSummary",arguments="{}"}}}};}
     else{if(!root.GetProperty("messages").EnumerateArray().Any(m=>m.GetProperty("role").GetString()=="tool"&&m.GetProperty("content").ToString().Contains("statuses")))throw new Exception("Actual database tool result not returned to provider");message=new{role="assistant",content="Protocol test completed using database tool output."};}
     var response=new{id="chatcmpl-test",@object="chat.completion",created=1700000000,model="protocol-test",choices=new[]{new{index=0,message,finish_reason=turn==0?"tool_calls":"stop"}},usage=new{prompt_tokens=10,completion_tokens=10,total_tokens=20}};
     var bytes=Encoding.UTF8.GetBytes(JsonSerializer.Serialize(response));context.Response.ContentType="application/json";context.Response.ContentLength64=bytes.Length;await context.Response.OutputStream.WriteAsync(bytes,timeout.Token);context.Response.Close();
    }
   },timeout.Token);
   var answer=await agent.Ask("Summarize the active upload",timeout.Token);await server;if(!answer.Contains("Protocol test completed")||!store.Logs().Any(x=>x.Tool=="GenerateOperationalSummary"))throw new Exception("Agent response or execution log missing");
  }finally{store.SaveSettings(previous);agent.Reset();listener.Stop();}
 }
}
