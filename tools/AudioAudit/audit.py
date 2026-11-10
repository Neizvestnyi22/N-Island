"""Run the actual RefreshLevels/SetVolume/ToggleMute methods against a fake COM endpoint.
This does not test Windows Core Audio hardware, threading or COM activation.
Requires Python 3 and dotnet 8 SDK on PATH (or DOTNET environment variable).
"""
from pathlib import Path
import tempfile,subprocess,os
root=Path(__file__).resolve().parents[2]
s=(root/'Services/AudioService.cs').read_text(encoding='utf-8-sig')
fragment=s[s.index('        public void RefreshLevels()'):s.index('        /// <summary>Микрофон по умолчанию')]
prefix='''using System;
class Endpoint {
 public float Value=.5f;public bool Muted;public bool Fail;
 public int GetMasterVolumeLevelScalar(out float v){v=Value;return 0;}
 public int GetMute(out bool m){m=Muted;return 0;}
 public int SetMasterVolumeLevelScalar(float v,ref Guid g){if(!Fail)Value=v;return Fail?-1:0;}
 public int SetMute(bool m,ref Guid g){if(!Fail)Muted=m;return Fail?-1:0;}
}
class Audio {
 public Endpoint _vol=new();public DateTime _selfChange;bool _init=true;
 public double Volume {get;private set;}=.5;public bool Muted {get;private set;}
 public event Action<double,bool,bool> Changed;
'''
tests='''}
class Program {static void Main(){
 var a=new Audio();int events=0;a.Changed+=(_,__,___)=>events++;
 a.SetVolume(a.Volume+.1);a.SetVolume(a.Volume+.1);
 if(Math.Abs(a.Volume-.7)>.001||events!=2)throw new Exception("rapid steps stale");
 a.ToggleMute();if(!a.Muted)throw new Exception("mute stale");
 a.SetVolume(.3);if(a.Muted||Math.Abs(a.Volume-.3)>.001)throw new Exception("unmute on positive write");
 a.SetVolume(2);if(a.Volume!=1)throw new Exception("upper bound");
 a.SetVolume(-2);if(a.Volume!=0)throw new Exception("lower bound");
 a.SetVolume(double.NaN);if(a.Volume!=0)throw new Exception("NaN");
 a._vol.Value=.8f;a._vol.Muted=true;a.RefreshLevels();if(!a.Muted||Math.Abs(a.Volume-.8)>.001)throw new Exception("external change");
 a._vol.Fail=true;a.SetVolume(.2);if(Math.Abs(a.Volume-.8)>.001)throw new Exception("reported unaccepted write");
 a._vol=null;a.SetVolume(.4);a.ToggleMute();a.RefreshLevels();
 Console.WriteLine("PASS 10 audio method scenarios: rapid steps, mute/unmute, bounds, NaN, external change, failure, no endpoint. Fake COM only.");
}}
'''
with tempfile.TemporaryDirectory(prefix='nisland-audio-') as td:
 p=Path(td);(p/'AudioAudit.csproj').write_text('<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net8.0</TargetFramework></PropertyGroup></Project>')
 (p/'Program.cs').write_text(prefix+fragment+tests)
 subprocess.run([os.environ.get('DOTNET','dotnet'),'run','--project',str(p/'AudioAudit.csproj')],check=True)
