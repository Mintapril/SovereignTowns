using System;
using System.Collections.Generic;
using System.Reflection;
using ImprovedGarrisons.Debugging.LogFileSystem;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.ViewModelCollection;
using TaleWorlds.Core;
using TaleWorlds.Core.ImageIdentifiers;
using TaleWorlds.ObjectSystem;

namespace ImprovedGarrisons.SaveSystem.SaveData.DataTypes;

[Serializable]
public class TrainingTemplate
{
	private Dictionary<string, int> troopList;

	private string _imageCharacterString;

	[NonSerialized]
	private Dictionary<CharacterObject, int> troopListWithCharacter;

	[NonSerialized]
	private List<CharacterObject> possibleUpgradeTroops;

	public string Name { get; set; }

	public int AmountOfTroopsInTemplate
	{
		get
		{
			if (troopList == null)
			{
				return 0;
			}
			return troopList.Count;
		}
	}

	public TrainingTemplate(string templateName)
	{
		Name = templateName;
		troopList = new Dictionary<string, int>();
		troopListWithCharacter = new Dictionary<CharacterObject, int>();
		possibleUpgradeTroops = new List<CharacterObject>();
	}

	public bool AddOrUpdateCharacter(CharacterObject character, int amount)
	{
		try
		{
			if (character == null || amount < 0 || troopList == null)
			{
				return false;
			}
			if (troopList.ContainsKey(((MBObjectBase)character).StringId))
			{
				troopList[((MBObjectBase)character).StringId] = amount;
				UpdateCharacterList();
				return true;
			}
			troopList.Add(((MBObjectBase)character).StringId, amount);
			UpdateCharacterList();
		}
		catch (Exception ex)
		{
			LogFileManager.WriteErrorLogEntry(MethodBase.GetCurrentMethod().Name, ex);
			return false;
		}
		return true;
	}

	public bool RemoveCharacter(CharacterObject character)
	{
		try
		{
			if (character == null || troopList == null)
			{
				return false;
			}
			if (troopList.ContainsKey(((MBObjectBase)character).StringId))
			{
				troopList.Remove(((MBObjectBase)character).StringId);
				UpdateCharacterList();
				return true;
			}
		}
		catch (Exception ex)
		{
			LogFileManager.WriteErrorLogEntry(MethodBase.GetCurrentMethod().Name, ex);
		}
		return false;
	}

	private void UpdateCharacterList()
	{
		try
		{
			troopListWithCharacter = new Dictionary<CharacterObject, int>();
			foreach (KeyValuePair<string, int> troop in troopList)
			{
				CharacterObject @object = MBObjectManager.Instance.GetObject<CharacterObject>(troop.Key);
				if (@object != null)
				{
					troopListWithCharacter.Add(@object, troop.Value);
				}
			}
			IEnumerable<CharacterObject> enumerable = CharacterObject.FindAll((Predicate<CharacterObject>)delegate(CharacterObject x)
			{
				bool flag = ((MBObjectBase)x).StringId.Contains("militia");
				return !((BasicCharacterObject)x).IsHero && ((BasicCharacterObject)x).IsSoldier && !flag;
			});
			possibleUpgradeTroops = new List<CharacterObject>();
			foreach (CharacterObject item in enumerable)
			{
				foreach (KeyValuePair<CharacterObject, int> item2 in troopListWithCharacter)
				{
					if (Main.UpgradeLogic.CharacterCanUpgradeToTarget(item, item2.Key))
					{
						possibleUpgradeTroops.Add(item);
					}
				}
			}
		}
		catch (Exception ex)
		{
			LogFileManager.WriteErrorLogEntry(MethodBase.GetCurrentMethod().Name, ex);
		}
	}

	public int GetAmountForTemplateTroop(CharacterObject character)
	{
		if (troopListWithCharacter.TryGetValue(character, out var value))
		{
			return value;
		}
		return 0;
	}

	public void Clear()
	{
		troopList.Clear();
		troopListWithCharacter = null;
		possibleUpgradeTroops = null;
	}

	public bool IsAValidUpgradeToop(CharacterObject character)
	{
		if (possibleUpgradeTroops == null)
		{
			UpdateCharacterList();
		}
		return possibleUpgradeTroops.Contains(character);
	}

	public bool Contains(CharacterObject character)
	{
		if (troopListWithCharacter == null)
		{
			UpdateCharacterList();
		}
		return troopListWithCharacter.ContainsKey(character);
	}

	public void SetTroops(Dictionary<CharacterObject, int> troops = null)
	{
		try
		{
			if (troops == null)
			{
				return;
			}
			int num = 0;
			CharacterObject val = null;
			troopList = new Dictionary<string, int>();
			foreach (KeyValuePair<CharacterObject, int> troop in troops)
			{
				troopList.Add(((MBObjectBase)troop.Key).StringId, troop.Value);
				if (troop.Value > num)
				{
					val = troop.Key;
				}
			}
			if (val != null)
			{
				_imageCharacterString = ((MBObjectBase)val).StringId;
			}
			UpdateCharacterList();
		}
		catch (Exception ex)
		{
			LogFileManager.WriteErrorLogEntry(MethodBase.GetCurrentMethod().Name, ex);
		}
	}

	public void SetTroops(Dictionary<string, int> troops = null)
	{
		try
		{
			if (troops == null)
			{
				return;
			}
			int num = 0;
			string text = null;
			troopList = new Dictionary<string, int>();
			foreach (KeyValuePair<string, int> troop in troops)
			{
				troopList.Add(troop.Key, troop.Value);
				if (troop.Value > num)
				{
					text = troop.Key;
				}
			}
			if (text != null)
			{
				_imageCharacterString = text;
			}
			UpdateCharacterList();
		}
		catch (Exception ex)
		{
			LogFileManager.WriteErrorLogEntry(MethodBase.GetCurrentMethod().Name, ex);
		}
	}

	public Dictionary<CharacterObject, int> GetTroopListAsCharacterObjects()
	{
		try
		{
			if (troopListWithCharacter == null)
			{
				UpdateCharacterList();
			}
			return troopListWithCharacter;
		}
		catch (Exception ex)
		{
			LogFileManager.WriteErrorLogEntry(MethodBase.GetCurrentMethod().Name, ex);
		}
		return null;
	}

	public Dictionary<string, int> GetTroopList()
	{
		try
		{
			return troopList;
		}
		catch (Exception ex)
		{
			LogFileManager.WriteErrorLogEntry(MethodBase.GetCurrentMethod().Name, ex);
		}
		return null;
	}

	public ImageIdentifier GetImage()
	{
		//IL_004d: Unknown result type (might be due to invalid IL or missing references)
		//IL_0058: Expected O, but got Unknown
		//IL_0053: Unknown result type (might be due to invalid IL or missing references)
		//IL_005a: Expected O, but got Unknown
		//IL_0033: Unknown result type (might be due to invalid IL or missing references)
		//IL_0039: Expected O, but got Unknown
		if (_imageCharacterString != null)
		{
			CharacterObject @object = MBObjectManager.Instance.GetObject<CharacterObject>(_imageCharacterString);
			if (@object != null)
			{
				CharacterImageIdentifier result = null;
				try
				{
					result = new CharacterImageIdentifier(CampaignUIHelper.GetCharacterCode(@object, false));
				}
				catch (Exception)
				{
				}
				return (ImageIdentifier)(object)result;
			}
		}
		return (ImageIdentifier)new BannerImageIdentifier(new Banner("11.116.1.1836.1836.768.788.1.0.-30.301.122.116.321.320.762.705.1.0.-180.318.122.116.321.320.764.803.1.0.-1"), false);
	}
}
