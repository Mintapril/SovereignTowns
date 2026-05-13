using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.Serialization.Formatters.Binary;
using System.Text.RegularExpressions;
using System.Xml.Serialization;
using ImprovedGarrisons.Debugging.LogFileSystem;
using ImprovedGarrisons.SaveSystem.SaveData.DataTypes;
using TaleWorlds.Library;

namespace ImprovedGarrisons.SaveSystem.Configuration;

public static class FileWriter
{
	private static BinaryFormatter binaryFormatter = new BinaryFormatter();

	private static int saveVersionNumber = 3;

	public static void SerializeObject<T>(this T toSerialize, string filename)
	{
		XmlSerializer xmlSerializer = new XmlSerializer(toSerialize.GetType());
		TextWriter textWriter = new StreamWriter(filename);
		xmlSerializer.Serialize(textWriter, toSerialize);
		textWriter.Close();
	}

	public static void SerializeToBin<Object>(Object dictionary, string filename)
	{
		try
		{
			Directory.CreateDirectory(Path.GetDirectoryName(filename));
			FileStream fileStream = new FileStream(filename, FileMode.Create);
			using (fileStream)
			{
				fileStream.WriteByte(Convert.ToByte(saveVersionNumber));
				binaryFormatter.Serialize(fileStream, dictionary);
			}
			fileStream.Close();
		}
		catch (IOException ex)
		{
			LogFileManager.WriteErrorLogEntry(MethodBase.GetCurrentMethod().Name, ex);
		}
	}

	public static Object DeserializeFromBin<Object>(string filename) where Object : new()
	{
		//IL_0066: Unknown result type (might be due to invalid IL or missing references)
		//IL_006b: Unknown result type (might be due to invalid IL or missing references)
		//IL_0075: Expected O, but got Unknown
		Object result = CreateInstance<Object>();
		try
		{
			FileStream fileStream = new FileStream(filename, FileMode.Open);
			FileInfo fileInfo = new FileInfo(filename);
			using (fileStream)
			{
				int num = fileStream.ReadByte();
				if (saveVersionNumber == num)
				{
					result = (Object)binaryFormatter.Deserialize(fileStream);
					fileStream.Close();
				}
				else
				{
					fileStream.Close();
					fileInfo.Delete();
					InformationManager.DisplayMessage(new InformationMessage("Improved Garrisons: Your garrison settings had to be reset because of an update. Please make sure to reconfigurate the settings for your garrisons", Color.FromUint(13897216u)));
				}
			}
		}
		catch (IOException ex)
		{
			LogFileManager.WriteErrorLogEntry(MethodBase.GetCurrentMethod().Name, ex);
		}
		return result;
	}

	public static Object CreateInstance<Object>() where Object : new()
	{
		return (Object)Activator.CreateInstance(typeof(Object));
	}

	public static string RemoveIllegalCharactersForFileCreation(string name)
	{
		string str = new string(Path.GetInvalidFileNameChars()) + new string(Path.GetInvalidPathChars());
		Regex regex = new Regex($"[{Regex.Escape(str)}]");
		return regex.Replace(name, "");
	}

	public static bool DeleteSaveFileByPath(string path)
	{
		if (File.Exists(path))
		{
			File.Delete(path);
			return true;
		}
		return false;
	}

	private static void DeleteIGSaveIfOld(Dictionary<string, GarrisonSettings> Dictionary)
	{
	}
}
