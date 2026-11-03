namespace MareSynchronos.Services.CharaData.Models;

public sealed record HandledCharaDataEntry(string Name, bool IsSelf, Guid? CustomizePlus, CharaDataMetaInfoExtendedDto MetaInfo, bool IsGpose = false)
{
    public CharaDataMetaInfoExtendedDto MetaInfo { get; set; } = MetaInfo;
}
