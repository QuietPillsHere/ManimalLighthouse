using System;

namespace Manimal.Lighthouse.Shared;

[Serializable]
public sealed class PayloadFile
{
    public string Path = "";
    public string Sha256 = "";
}