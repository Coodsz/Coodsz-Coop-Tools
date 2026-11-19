using System.Runtime.CompilerServices;
using MCM.Abstractions.Attributes;
using MCM.Abstractions.Attributes.v2;
using MCM.Abstractions.Base.Global;

namespace CoopMarriage;

public sealed class Settings : AttributeGlobalSettings<Settings>
{
	public override string Id => "CoopMarriagePolygamy";

	public override string DisplayName => "COOP Family";

	public override string FolderName => "CoopMarriage";

	public override string FormatType => "json2";

	[SettingPropertyGroup("{=coop_marriage_mcm_group_pregnancy}Pregnancy & Birth")]
	[SettingPropertyBool("{=coop_marriage_mcm_pregnancies}Enable pregnancies", HintText = "{=coop_marriage_mcm_pregnancies_hint}Enables server-authoritative Coop pregnancy and birth.", Order = 0, RequireRestart = false)]
	[field: CompilerGenerated]
	public bool EnablePregnancies
	{
		[CompilerGenerated]
		get;
		[CompilerGenerated]
		set;
	} = true;

	[SettingPropertyGroup("{=coop_marriage_mcm_group_pregnancy}Pregnancy & Birth")]
	[SettingPropertyInteger("{=coop_marriage_mcm_duration}Pregnancy duration (days)", 11, 21, "0", HintText = "{=coop_marriage_mcm_duration_hint}Campaign days between conception and birth.", Order = 1, RequireRestart = false)]
	[field: CompilerGenerated]
	public int PregnancyDurationDays
	{
		[CompilerGenerated]
		get;
		[CompilerGenerated]
		set;
	} = 21;

	[SettingPropertyGroup("{=coop_marriage_mcm_group_pregnancy}Pregnancy & Birth")]
	[SettingPropertyInteger("{=coop_marriage_mcm_max_children}Maximum children per spouse", 0, 12, "0", HintText = "{=coop_marriage_mcm_max_children_hint}Maximum number of children each current or former wife can have.", Order = 2, RequireRestart = false)]
	[field: CompilerGenerated]
	public int MaximumChildrenPerMother
	{
		[CompilerGenerated]
		get;
		[CompilerGenerated]
		set;
	} = 12;

	internal static Settings Current => GlobalSettings<Settings>.Instance ?? new Settings();
}
