using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
namespace Toma.Windows;
internal static class AiProviderTests {
 internal static void Run(Action<string,Func<bool>> test) {
  test("AI backups: paid OpenRouter rejected",()=>{try{AiProviders.Validate("openrouter","openai/gpt-4");return false;}catch(GeminiFailure){return true;}});
  test("AI backups: free router accepted",()=>AiProviders.Validate("openrouter","openrouter/free")=="openrouter/free");
  test("AI backups: Groq explicit reset respected",()=>{using var response=new HttpResponseMessage((HttpStatusCode)429);response.Headers.Add("x-ratelimit-remaining-requests","0");response.Headers.Add("x-ratelimit-reset-requests","1h");var now=new DateTimeOffset(2026,11,13,1,0,0,TimeSpan.Zero);var quota=GeminiRequestGate.Parse(response,"{}",now,"groq");return quota.Daily&&quota.Until==now.AddHours(1);});
  test("AI backups: Gemini quota falls back once to Groq",()=>Run(async()=>{var calls=new List<string>();var settings=new Settings{AiBackups=new(){new(){Provider="groq",Model=AiProviders.DefaultModel("groq"),Enabled=true},new(){Provider="openrouter",Model="openrouter/free",Enabled=true}}};var chain=new AiFailover(settings,_=>"fixture",(id,key,model,title,artist,lines,ids,token,status)=>{calls.Add(id);if(id=="gemini")throw new GeminiRateLimited(DateTimeOffset.UtcNow.AddMinutes(1),false);return Task.FromResult(new[]{new GeminiRow(0,"Vietnam",true,"sin caw")});});var result=await chain.Batch("","",new[]{new LyricLine(0,"Xin chao")},new[]{0},CancellationToken.None,null);return result.Length==1&&calls.SequenceEqual(new[]{"gemini","groq"});}));
  test("AI backups: missing keys skipped",()=>Run(async()=>{int calls=0;var chain=new AiFailover(new Settings(),_=>"",(a,b,c,d,e,f,g,h,i)=>{calls++;throw new Exception();});try{await chain.Batch("","",Array.Empty<LyricLine>(),Array.Empty<int>(),CancellationToken.None,null);return false;}catch(GeminiFailure){return calls==0;}}));
  test("AI backups: cancellation stops chain",()=>Run(async()=>{int calls=0;using var cancel=new CancellationTokenSource();cancel.Cancel();var chain=new AiFailover(new Settings(),_=>"fixture",(a,b,c,d,e,f,g,h,i)=>{calls++;throw new Exception();});try{await chain.Batch("","",Array.Empty<LyricLine>(),Array.Empty<int>(),cancel.Token,null);return false;}catch(OperationCanceledException){return calls==0;}}));
 }
 private static bool Run(Func<Task<bool>> task)=>Task.Run(task).GetAwaiter().GetResult();
}
