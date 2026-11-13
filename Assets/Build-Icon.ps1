param([string]$Destination = $PSScriptRoot)
$ErrorActionPreference = 'Stop'
if ($PSVersionTable.PSEdition -eq 'Core') {
    & "$env:WINDIR\System32\WindowsPowerShell\v1.0\powershell.exe" -NoProfile -ExecutionPolicy Bypass -File $PSCommandPath -Destination $Destination
    if ($LASTEXITCODE -ne 0) { throw 'Icon generation failed.' }
    return
}
Add-Type -AssemblyName System.Drawing
Add-Type -ReferencedAssemblies System.Drawing -TypeDefinition @"
using System;
using System.IO;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
public static class AppLockIconBuilder
{
    static GraphicsPath Round(float x,float y,float w,float h,float r)
    {
        var p=new GraphicsPath(); float d=2*r;
        p.AddArc(x,y,d,d,180,90); p.AddArc(x+w-d,y,d,d,270,90);
        p.AddArc(x+w-d,y+h-d,d,d,0,90); p.AddArc(x,y+h-d,d,d,90,90);p.CloseFigure();return p;
    }
    static Color C(string hex) { return ColorTranslator.FromHtml(hex); }
    static Bitmap Draw(int size)
    {
        var b=new Bitmap(size,size,PixelFormat.Format32bppArgb);
        using(var g=Graphics.FromImage(b))
        {
            g.Clear(Color.Transparent);g.SmoothingMode=SmoothingMode.AntiAlias;g.PixelOffsetMode=PixelOffsetMode.HighQuality;g.ScaleTransform(size/256f,size/256f);
            using(var tile=Round(8,8,240,240,54))
            using(var bg=new LinearGradientBrush(new Point(20,8),new Point(220,248),C("#203B52"),C("#09131F")))
            using(var edge=new Pen(C("#3A6078"),2)) {g.FillPath(bg,tile);g.DrawPath(edge,tile);}
            using(var shackle=new GraphicsPath())
            using(var pen=new Pen(C("#B1EDF4"),17))
            {
                shackle.AddLine(88,119,88,94);shackle.AddBezier(88,94,88,43,168,43,168,94);shackle.AddLine(168,94,168,119);
                pen.StartCap=LineCap.Round;pen.EndCap=LineCap.Round;g.DrawPath(pen,shackle);
            }
            using(var body=Round(65,108,126,101,24))
            using(var fill=new LinearGradientBrush(new Point(72,110),new Point(184,209),C("#69E3F1"),C("#16A4BE")))
            using(var border=new Pen(Color.FromArgb(130,160,248,255),1.5f)) {g.FillPath(fill,body);g.DrawPath(border,body);}
            using(var shine=new Pen(Color.FromArgb(100,235,255,255),2))g.DrawLine(shine,88,122,168,122);
            using(var key=new SolidBrush(C("#0D3347")))
            using(var stem=Round(123,154,10,23,4)){g.FillEllipse(key,117,142,22,22);g.FillPath(key,stem);}
        }
        return b;
    }
    static byte[] Dib(Bitmap bitmap)
    {
        int size=bitmap.Width, maskStride=((size+31)/32)*4;
        using(var stream=new MemoryStream())
        using(var writer=new BinaryWriter(stream))
        {
            writer.Write(40);writer.Write(size);writer.Write(size*2);writer.Write((ushort)1);writer.Write((ushort)32);
            writer.Write(0);writer.Write(size*size*4+maskStride*size);writer.Write(0);writer.Write(0);writer.Write(0);writer.Write(0);
            for(int y=size-1;y>=0;y--)for(int x=0;x<size;x++){Color c=bitmap.GetPixel(x,y);writer.Write(c.B);writer.Write(c.G);writer.Write(c.R);writer.Write(c.A);}
            for(int y=size-1;y>=0;y--){byte[] row=new byte[maskStride];for(int x=0;x<size;x++)if(bitmap.GetPixel(x,y).A==0)row[x/8]|=(byte)(0x80>>(x%8));writer.Write(row);}
            return stream.ToArray();
        }
    }
    public static void Build(string destination)
    {
        Directory.CreateDirectory(destination);
        using(var preview=Draw(512))preview.Save(Path.Combine(destination,"AppLock.png"),ImageFormat.Png);
        int[] sizes={16,20,24,32,40,48,64,128,256};var frames=new List<byte[]>();
        foreach(int size in sizes)using(var bitmap=Draw(size))
        {
            if(size<256)frames.Add(Dib(bitmap));
            else using(var stream=new MemoryStream()){bitmap.Save(stream,ImageFormat.Png);frames.Add(stream.ToArray());}
        }
        using(var preview=Draw(128))preview.Save(Path.Combine(destination,"AppLock-preview.png"),ImageFormat.Png);
        using(var writer=new BinaryWriter(File.Create(Path.Combine(destination,"AppLock.ico"))))
        {
            writer.Write((ushort)0);writer.Write((ushort)1);writer.Write((ushort)sizes.Length);
            int offset=6+16*sizes.Length;
            for(int i=0;i<sizes.Length;i++) {writer.Write((byte)(sizes[i]==256?0:sizes[i]));writer.Write((byte)(sizes[i]==256?0:sizes[i]));writer.Write((byte)0);writer.Write((byte)0);writer.Write((ushort)1);writer.Write((ushort)32);writer.Write(frames[i].Length);writer.Write(offset);offset+=frames[i].Length;}
            foreach(var frame in frames)writer.Write(frame);
        }
    }
}
"@
[AppLockIconBuilder]::Build($Destination)
