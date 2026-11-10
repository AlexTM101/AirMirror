// GPL-3.0. Build-time only. Uses MIT-licensed Iced to relocate verified prologues.
using System;
using System.IO;
using System.Text;
using System.Collections.Generic;
using Iced.Intel;

class PresentationPatcher {
    sealed class Writer : CodeWriter { public readonly List<byte> Bytes = new List<byte>(); public override void WriteByte(byte value) { Bytes.Add(value); } }
    sealed class Hook { public string Name, Macro, Signature; public uint Rva, Thunk, Trampoline, Unwind; public int Stolen; public byte[] Relocated; }
    byte[] image; int optional, sectionTable, sectionCount;
    uint U(int p) { return BitConverter.ToUInt32(image,p); }
    void Set(int p,uint v) { Array.Copy(BitConverter.GetBytes(v),0,image,p,4); }
    static uint Align(uint n,uint alignment) { return (n+alignment-1)/alignment*alignment; }
    int Raw(uint rva) { for(int i=0;i<sectionCount;i++){int p=sectionTable+i*40;uint begin=U(p+12);if(rva>=begin&&rva<begin+U(p+16))return checked((int)(U(p+20)+rva-begin));}throw new Exception("RVA outside image"); }
    static void Put(MemoryStream stream,uint value) { byte[] bytes=BitConverter.GetBytes(value);stream.Write(bytes,0,4); }
    static void Put64(MemoryStream stream,ulong value) { byte[] bytes=BitConverter.GetBytes(value);stream.Write(bytes,0,8); }
    static void Text(MemoryStream stream,string value) { byte[] bytes=Encoding.ASCII.GetBytes(value+"\0");stream.Write(bytes,0,bytes.Length); }
    static void Pad(MemoryStream stream,int alignment,byte value) { while(stream.Length%alignment!=0)stream.WriteByte(value); }
    void Section(int index,string name,uint rva,uint size,uint raw,uint rawSize,uint flags) {
        int p=sectionTable+index*40; Array.Clear(image,p,40); byte[] text=Encoding.ASCII.GetBytes(name);Array.Copy(text,0,image,p,text.Length);
        Set(p+8,size);Set(p+12,rva);Set(p+16,rawSize);Set(p+20,raw);Set(p+36,flags);
    }
    void Run(string path,string header) {
        image=File.ReadAllBytes(path);int pe=(int)U(0x3c);optional=pe+24;sectionCount=BitConverter.ToUInt16(image,pe+6);
        if(U(pe)!=0x4550||BitConverter.ToUInt16(image,optional)!=0x20b||sectionCount!=6)throw new Exception("Expected pinned x64 engine before presentation patch");
        sectionTable=optional+BitConverter.ToUInt16(image,pe+20);
        uint fileAlignment=U(optional+36),memoryAlignment=U(optional+32),firstRaw=UInt32.MaxValue,lastVirtual=0;
        for(int i=0;i<sectionCount;i++){int p=sectionTable+i*40;firstRaw=Math.Min(firstRaw,U(p+20)==0?UInt32.MaxValue:U(p+20));lastVirtual=Math.Max(lastVirtual,U(p+12)+Math.Max(U(p+8),U(p+16)));}
        if(sectionTable+(sectionCount+2)*40>firstRaw)throw new Exception("No space for two section headers");
        var hooks=new[]{
            new Hook{Name="AM_PushFrame",Macro="AM_ORIGINAL_PUSH_RVA",Rva=0x5e5d0,Signature="48-85-D2-0F-84-83-01-00-00"},
            new Hook{Name="AM_ClearFrames",Macro="AM_ORIGINAL_CLEAR_RVA",Rva=0x5c340,Signature="40-53-55-56-57"},
            new Hook{Name="AM_StopFrames",Macro="AM_ORIGINAL_STOP_RVA",Rva=0x68070,Signature="40-53-48-83-EC-30"},
            new Hook{Name="AM_DestroyFrames",Macro="AM_ORIGINAL_DESTROY_RVA",Rva=0x59b80,Signature="48-89-5C-24-08"}
        };
        uint oldExceptionRva=U(optional+136),oldExceptionSize=U(optional+140);int oldException=Raw(oldExceptionRva);
        foreach(var h in hooks){h.Stolen=h.Signature.Split('-').Length;int raw=Raw(h.Rva);if(BitConverter.ToString(image,raw,h.Stolen)!=h.Signature)throw new Exception("Unexpected renderer prologue: "+h.Name);for(int i=0;i<oldExceptionSize;i+=12)if(U(oldException+i)==h.Rva)h.Unwind=U(oldException+i+8);if(h.Unwind==0)throw new Exception("Renderer unwind metadata missing");}
        int oldImports=Raw(U(optional+120)),descriptors=0;while(U(oldImports+descriptors*20+12)!=0)descriptors++;
        uint dataRva=Align(lastVirtual,memoryAlignment);
        var data=new MemoryStream();data.SetLength((descriptors+2)*20);data.Position=data.Length;
        uint dllName=(uint)data.Position;Text(data,"airnet.dll");Pad(data,8,0);
        uint lookup=(uint)data.Position;data.SetLength(data.Length+(hooks.Length+1)*8);data.Position=data.Length;
        uint iat=(uint)data.Position;data.SetLength(data.Length+(hooks.Length+1)*8);data.Position=data.Length;
        var names=new uint[hooks.Length];for(int i=0;i<hooks.Length;i++){Pad(data,2,0);names[i]=(uint)data.Position;data.WriteByte(0);data.WriteByte(0);Text(data,hooks[i].Name);}
        Pad(data,4,0);uint exceptionOffset=(uint)data.Position;data.SetLength(data.Length+oldExceptionSize+hooks.Length*12);data.Position=data.Length;
        uint codeRva=Align(dataRva+(uint)data.Length,memoryAlignment);var code=new MemoryStream();
        for(int i=0;i<hooks.Length;i++){
            Hook h=hooks[i];Pad(code,16,0x90);h.Thunk=codeRva+(uint)code.Position;code.WriteByte(0xff);code.WriteByte(0x25);Put(code,unchecked(dataRva+iat+(uint)i*8-(h.Thunk+6)));
            Pad(code,16,0x90);h.Trampoline=codeRva+(uint)code.Position;
            byte[] prefix=new byte[h.Stolen];Array.Copy(image,Raw(h.Rva),prefix,0,prefix.Length);var decoder=Iced.Intel.Decoder.Create(64,new ByteArrayCodeReader(prefix));decoder.IP=h.Rva;
            var instructions=new List<Instruction>();while(decoder.IP<h.Rva+(uint)h.Stolen){Instruction ins;decoder.Decode(out ins);if(ins.Code==Code.INVALID)throw new Exception("Invalid prologue instruction");instructions.Add(ins);}if(decoder.IP!=h.Rva+(uint)h.Stolen)throw new Exception("Prologue ends inside instruction");
            instructions.Add(Instruction.CreateBranch(Code.Jmp_rel32_64,h.Rva+(uint)h.Stolen));var writer=new Writer();string error;BlockEncoderResult result;
            if(!BlockEncoder.TryEncode(64,new InstructionBlock(writer,instructions,h.Trampoline),out error,out result))throw new Exception("Cannot relocate prologue: "+error);
            h.Relocated=writer.Bytes.ToArray();code.Write(h.Relocated,0,h.Relocated.Length);
        }
        byte[] body=data.ToArray();Array.Copy(image,oldImports,body,0,descriptors*20);
        Action<int,uint> setData=delegate(int p,uint v){Array.Copy(BitConverter.GetBytes(v),0,body,p,4);};
        int descriptor=descriptors*20;setData(descriptor,dataRva+lookup);setData(descriptor+12,dataRva+dllName);setData(descriptor+16,dataRva+iat);
        for(int i=0;i<hooks.Length;i++){Array.Copy(BitConverter.GetBytes((ulong)(dataRva+names[i])),0,body,lookup+i*8,8);Array.Copy(BitConverter.GetBytes((ulong)(dataRva+names[i])),0,body,iat+i*8,8);}
        Array.Copy(image,oldException,body,exceptionOffset,oldExceptionSize);
        for(int i=0;i<hooks.Length;i++){int p=(int)(exceptionOffset+oldExceptionSize+i*12);setData(p,hooks[i].Trampoline);setData(p+4,hooks[i].Trampoline+(uint)hooks[i].Relocated.Length);setData(p+8,hooks[i].Unwind);}
        uint dataRaw=Align((uint)image.Length,fileAlignment),dataRawSize=Align((uint)body.Length,fileAlignment),codeRaw=dataRaw+dataRawSize,codeRawSize=Align((uint)code.Length,fileAlignment);
        Array.Resize(ref image,checked((int)(codeRaw+codeRawSize)));Array.Copy(body,0,image,dataRaw,body.Length);Array.Copy(code.ToArray(),0,image,codeRaw,(int)code.Length);
        foreach(var h in hooks){int p=Raw(h.Rva);image[p]=0xe9;Set(p+1,unchecked(h.Thunk-h.Rva-5));for(int i=5;i<h.Stolen;i++)image[p+i]=0x90;}
        Section(sectionCount,".amimp",dataRva,(uint)body.Length,dataRaw,dataRawSize,0xc0000040);Section(sectionCount+1,".amcode",codeRva,(uint)code.Length,codeRaw,codeRawSize,0x60000020);
        Array.Copy(BitConverter.GetBytes((ushort)(sectionCount+2)),0,image,pe+6,2);
        Set(optional+4,U(optional+4)+codeRawSize);Set(optional+8,U(optional+8)+dataRawSize);Set(optional+56,Align(codeRva+(uint)code.Length,memoryAlignment));Set(optional+64,0);
        Set(optional+120,dataRva);Set(optional+124,(uint)((descriptors+2)*20));Set(optional+136,dataRva+exceptionOffset);Set(optional+140,oldExceptionSize+(uint)hooks.Length*12);
        var generated=new StringBuilder("/* Generated for the checksum-pinned engine. GPL-3.0. */\n");foreach(var h in hooks)generated.AppendFormat("#define {0} 0x{1:x}u\n",h.Macro,h.Trampoline);
        File.WriteAllText(header,generated.ToString(),Encoding.ASCII);File.WriteAllBytes(path,image);
        foreach(var h in hooks)Console.WriteLine("Verified renderer bridge {0}: {1:X} -> {2:X}; original trampoline {3:X}",h.Name,h.Rva,h.Thunk,h.Trampoline);
    }
    static int Main(string[] args){try{new PresentationPatcher().Run(args[0],args[1]);return 0;}catch(Exception e){Console.Error.WriteLine(e.Message);return 1;}}
}
