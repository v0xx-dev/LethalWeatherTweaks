using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using HarmonyLib;
using Newtonsoft.Json;
using UnityEngine;
using WeatherRegistry;
using WeatherTweaks.Definitions;
using WeatherTweaks.Patches;
using static WeatherTweaks.Modules.Types;
using WeatherType = WeatherTweaks.Definitions.WeatherType;

namespace WeatherTweaks
{
  internal class Variables
  {
    internal static List<SelectableLevel> GameLevels = [];

    internal static bool IsSetupFinished = false;

    internal static WeatherType NoneWeather;
    public static List<WeatherType> WeatherTypes = [];

    public static List<Definitions.Types.CombinedWeatherType> CombinedWeatherTypes = [];
    public static List<Definitions.Types.ProgressingWeatherType> ProgressingWeatherTypes = [];

    public static Dictionary<SelectableLevel, WeatherType> CurrentWeathers = [];
    public static List<ImprovedWeatherEffect> CurrentEffects = [];

    public static WeatherType CurrentLevelWeather;

    internal static WeatherType GetCurrentWeather()
    {
      if (CurrentLevelWeather.Type == CustomWeatherType.Progressing)
      {
        if (ChangeMidDay.currentEntry == null)
        {
          Plugin.logger.LogWarning("Current entry is null");
          return CurrentLevelWeather;
        }

        return ChangeMidDay.currentEntry.GetWeatherType();
      }

      return CurrentLevelWeather;
    }

    internal static Dictionary<int, LevelWeatherType> GetWeatherData(string weatherData)
    {
      Dictionary<int, LevelWeatherType> weatherDataDict = JsonConvert.DeserializeObject<Dictionary<int, LevelWeatherType>>(weatherData);
      return weatherDataDict;
    }

    internal static List<SelectableLevel> GetGameLevels(bool includeCompanyMoon = false)
    {
      Plugin.logger.LogDebug($"Getting game levels, {includeCompanyMoon}");
      List<SelectableLevel> GameLevels = MrovLib.API.SharedMethods.GetGameLevels();

      if (!includeCompanyMoon)
      {
        GameLevels = GameLevels.Where(level => level.PlanetName != "71 Gordion").ToList();
      }

      Variables.GameLevels = GameLevels;
      return GameLevels;
    }

    internal static List<LevelWeatherType> GetPlanetPossibleWeathers(SelectableLevel level)
    {
      if (level.randomWeathers == null || level.randomWeathers.Count() == 0)
      {
        Plugin.logger.LogError("Random weathers are null");
        return [];
      }

      List<LevelWeatherType> weathersToChooseFrom = level
        .randomWeathers.Where(randomWeather =>
          randomWeather.weatherType != LevelWeatherType.None && randomWeather.weatherType != LevelWeatherType.DustClouds
        )
        .ToList()
        .Select(x => x.weatherType)
        .Append(LevelWeatherType.None)
        .ToList();

      return weathersToChooseFrom;
    }

    internal static List<WeatherType> GetPlanetWeatherTypes(SelectableLevel level)
    {
      List<LevelWeatherType> randomWeathers = GetPlanetPossibleWeathers(level);

      if (randomWeathers.Count() == 0)
      {
        Plugin.logger.LogError("Random weathers are empty");
        return [];
      }

      List<WeatherType> possibleTypes = [];

      foreach (WeatherType weather in WeatherTypes)
      {
        if (randomWeathers.Contains(weather.weatherType) && weather.Type == CustomWeatherType.Normal)
        {
          possibleTypes.Add(weather);
        }

        switch (weather.Type)
        {
          case CustomWeatherType.Combined:
            Definitions.Types.CombinedWeatherType combinedWeather = CombinedWeatherTypes.Find(x => x.Name == weather.Name);
            if (combinedWeather.CanWeatherBeApplied(level))
            {
              possibleTypes.Add(weather);
            }
            break;
          case CustomWeatherType.Progressing:
            Definitions.Types.ProgressingWeatherType progressingWeather = ProgressingWeatherTypes.Find(x => x.Name == weather.Name);
            if (progressingWeather.Enabled.Value == false)
            {
              Plugin.logger.LogDebug($"Progressing weather: {progressingWeather.Name} is disabled");
              continue;
            }

            if (progressingWeather.CanWeatherBeApplied(level))
            {
              possibleTypes.Add(weather);
            }
            break;
        }
      }

      // Plugin.logger.LogDebug($"Possible types: {string.Join("; ", possibleTypes.Select(x => x.Name))}");
      return possibleTypes.Distinct().ToList();
    }

    internal static Dictionary<string, WeatherType> GetAllPlanetWeathersDictionary()
    {
      Dictionary<string, WeatherType> weathers = [];

      CurrentWeathers
        .ToList()
        .ForEach(weather =>
        {
          weathers.Add(weather.Key.PlanetName, weather.Value);
        });

      return weathers;
    }

    internal static void PopulateWeathers(StartOfRound startOfRound)
    {
      Plugin.logger.LogDebug("Populating weathers");

      if (TimeOfDay.Instance == null)
      {
        Plugin.logger.LogError("TimeOfDay is null");
        return;
      }

      WeatherEffect[] effects = TimeOfDay.Instance.effects;

      WeatherTypes.Clear();

      if (effects == null || effects.Count() == 0)
      {
        Plugin.logger.LogWarning("Effects are null");
      }

      NoneWeather = new("None", CustomWeatherType.Normal) { Weather = WeatherManager.NoneWeather, weatherType = LevelWeatherType.None, };
      WeatherTypes.Add(NoneWeather);

      foreach (Weather weather in WeatherRegistry.WeatherManager.Weathers)
      {
        WeatherType newWeather = new(weather.name, CustomWeatherType.Normal) { Weather = weather, weatherType = weather.VanillaWeatherType, };

        WeatherTypes.Add(newWeather);
      }

      CombinedWeatherTypes.ForEach(combinedWeather =>
      {
        if (combinedWeather.Enabled.Value == false)
        {
          Plugin.logger.LogDebug($"Combined weather: {combinedWeather.Name} is disabled");
          return;
        }
        Plugin.logger.LogDebug($"Adding combined weather: {combinedWeather.Name}");

        WeatherTypes.Add(combinedWeather);
      });

      ProgressingWeatherTypes.ForEach(progressingWeather =>
      {
        if (progressingWeather.Enabled.Value == false)
        {
          Plugin.logger.LogDebug($"Progressing weather: {progressingWeather.Name} is disabled");
          return;
        }

        Plugin.logger.LogDebug($"Adding progressing weather: {progressingWeather.Name}");

        WeatherTypes.Add(progressingWeather);
      });
    }

    public static string GetPlanetCurrentWeather(SelectableLevel level, bool uncertain = true)
    {
      bool isUncertainWeather = UncertainWeather.uncertainWeathers.ContainsKey(level.PlanetName);

      if (isUncertainWeather && uncertain)
      {
        return UncertainWeather.uncertainWeathers[level.PlanetName];
      }
      else
      {
        if (CurrentWeathers.ContainsKey(level) == false)
        {
          return level.currentWeather.ToString();
        }

        return CurrentWeathers[level].Name;
      }
    }

    public static WeatherType GetPlanetCurrentWeatherType(SelectableLevel level)
    {
      return GetFullWeatherType(CurrentWeathers.TryGetValue(level, out WeatherType weather) ? weather : NoneWeather);
    }

    public static float GetLevelWeatherVariable(LevelWeatherType weatherType, bool variable2 = false)
    {
      if (StartOfRound.Instance == null)
      {
        Plugin.logger.LogError("StartOfRound is null");
        return 0;
      }

      SelectableLevel level = StartOfRound.Instance.currentLevel;
      RandomWeatherWithVariables randomWeather = level.randomWeathers.First(x => x.weatherType == weatherType);

      if (randomWeather == null || StartOfRound.Instance == null || level == null)
      {
        Plugin.logger.LogError($"Failed to get weather variables for {level.PlanetName}:{weatherType}");
        return 0;
      }

      Plugin.logger.LogDebug(
        $"Got weather variables for {level.PlanetName}:{weatherType} with variables {randomWeather.weatherVariable} {randomWeather.weatherVariable2}"
      );

      if (variable2)
      {
        return randomWeather.weatherVariable2;
      }

      return randomWeather.weatherVariable;
    }

    // public static LevelWeatherType LevelHasWeather(LevelWeatherType weatherType)
    // {
    //   Weather currentWeather = WeatherManager.GetWeather(weatherType);
    //   SelectableLevel level = StartOfRound.Instance.currentLevel;

    //   if (StartOfRound.Instance == null || level == null)
    //   {
    //     Plugin.logger.LogError($"Failed to get weather variables for {level.PlanetName}:{weatherType}");
    //     return LevelWeatherType.None;
    //   }

    //   LevelWeatherVariables weatherVariables = currentWeather.WeatherVariables.GetValueOrDefault(level);

    //   if (weatherVariables != null)
    //   {
    //     Plugin.logger.LogDebug($"Weather variable: {weatherVariables}");
    //     return weatherType;
    //   }

    //   return LevelWeatherType.None;
    // }

    public static LevelWeatherType LevelHasWeather(LevelWeatherType weatherType)
    {
      SelectableLevel level = StartOfRound.Instance.currentLevel;
      if (StartOfRound.Instance == null || level == null)
      {
        Plugin.logger.LogError($"Failed to get weather variables for {level.PlanetName}:{weatherType}");
        return LevelWeatherType.None;
      }

      WeatherType currentWeather = GetFullWeatherType(CurrentWeathers.TryGetValue(level, out WeatherType weather) ? weather : NoneWeather);

      Plugin.logger.LogDebug(currentWeather.GetType().ToString());

      switch (currentWeather.GetType().ToString())
      {
        case "WeatherTweaks.Definitions.Types+CombinedWeatherType":
          Definitions.Types.CombinedWeatherType combinedWeather = (Definitions.Types.CombinedWeatherType)currentWeather;
          if (combinedWeather.Weathers.Any(x => x.VanillaWeatherType == weatherType))
          {
            Plugin.logger.LogWarning($"Level {level.PlanetName} has weather {weatherType}");
            return weatherType;
          }
          break;
        case "WeatherTweaks.Definitions.Types+ProgressingWeatherType":
          Definitions.Types.ProgressingWeatherType progressingWeather = (Definitions.Types.ProgressingWeatherType)currentWeather;

          if (progressingWeather.DoesHaveWeatherHappening(weatherType))
          {
            Plugin.logger.LogWarning($"Level {level.PlanetName} has weather {weatherType}");
            return weatherType;
          }
          break;

        default:
          if (currentWeather.Weather.VanillaWeatherType == weatherType)
          {
            Plugin.logger.LogWarning($"Level {level.PlanetName} has weather {weatherType}");
            return weatherType;
          }
          break;
      }
      return LevelWeatherType.None;
    }

    internal static WeatherType GetVanillaWeatherType(LevelWeatherType weatherType)
    {
      return WeatherTypes.Find(x => x.weatherType == weatherType && x.Type == CustomWeatherType.Normal);
    }

    internal static WeatherType GetFullWeatherType(WeatherType weatherType)
    {
      Plugin.logger.LogDebug($"Getting full weather type for {weatherType.Name}");
      return WeatherTypes.Find(x => x.Name == weatherType.Name);
    }

    internal static List<WeatherType> GetPlanetWeightedList(
      SelectableLevel level,
      Dictionary<LevelWeatherType, int> weights,
      float difficulty = 0
    )
    {
      var weatherList = new List<WeatherType>();

      difficulty = Math.Clamp(difficulty, 0, ConfigManager.MaxMultiplier.Value);

      List<WeatherType> weatherTypes = GetPlanetWeatherTypes(level);
      foreach (var weather in weatherTypes)
      {
        var weatherType = weather;
        var weatherWeight = weights.TryGetValue(weather.weatherType, out int weight) ? weight : weather.Weather.DefaultWeight;

        if (ConfigManager.ScaleDownClearWeather.Value && weather.weatherType == LevelWeatherType.None)
        {
          int clearWeatherWeight = weights[LevelWeatherType.None];
          int fullWeightSum = weights.Sum(x => x.Value);

          int possibleWeathersWeightSum = 0;
          level
            .randomWeathers.ToList()
            .Where(randomWeather => randomWeather.weatherType != LevelWeatherType.DustClouds)
            .ToList()
            .ForEach(randomWeather =>
            {
              possibleWeathersWeightSum += weights.TryGetValue(randomWeather.weatherType, out int weight)
                ? weight
                : weather.Weather.DefaultWeight;
            });
          // proportion from clearWeatherWeight / fullWeightsSum

          double noWetherFinalWeight = (double)(clearWeatherWeight * Math.Max(possibleWeathersWeightSum, 1) / fullWeightSum);
          weatherWeight = Convert.ToInt32(noWetherFinalWeight);

          Plugin.logger.LogDebug(
            $"Scaling down clear weather weight from {clearWeatherWeight} to {weatherWeight} : ({clearWeatherWeight} * {possibleWeathersWeightSum} / {fullWeightSum}) == {weatherWeight}"
          );
        }

        if (weatherType.Type == CustomWeatherType.Combined)
        {
          var combinedWeather = CombinedWeatherTypes.Find(x => x.Name == weatherType.Name);

          if (combinedWeather.CanWeatherBeApplied(level))
          {
            weatherWeight = Mathf.RoundToInt(
              (weights.TryGetValue(weather.weatherType, out int localWeight) ? localWeight : weather.Weather.DefaultWeight)
                * combinedWeather.weightModify
            );
          }
          else
          {
            Plugin.logger.LogDebug($"Combined weather: {combinedWeather.Name} can't be applied");
            continue;
          }
        }

        if (weatherType.Type == CustomWeatherType.Progressing)
        {
          var progressingWeather = ProgressingWeatherTypes.Find(x => x.Name == weatherType.Name);
          if (progressingWeather.Enabled.Value == false)
          {
            Plugin.logger.LogDebug($"Progressing weather: {progressingWeather.Name} is disabled");
            continue;
          }

          if (progressingWeather.CanWeatherBeApplied(level) == false)
          {
            Plugin.logger.LogDebug($"Progressing weather: {progressingWeather.Name} can't be applied");
            continue;
          }

          weatherWeight = Mathf.RoundToInt(
            weights.TryGetValue(weather.weatherType, out int localWeight)
              ? localWeight
              : weather.Weather.DefaultWeight * progressingWeather.weightModify
          );
        }

        if (difficulty != 0 && weatherType.weatherType == LevelWeatherType.None)
        {
          weatherWeight = (int)(weatherWeight * (1 - difficulty));
        }

        Plugin.logger.LogDebug($"{weatherType.Name} has weight {weatherWeight}");

        for (var i = 0; i < weatherWeight; i++)
        {
          weatherList.Add(weather);
        }
      }

      return weatherList;
    }
  }
}
