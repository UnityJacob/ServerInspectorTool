using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(order=0, fileName = "new matchmaking config", menuName = "Unity/UCG/Matchmaking Server Config")]
public class MatchmakingConfigAsset : ScriptableObject
{
   public string MatchmakingURL = "cloud.connected.unity3d.com";
   public string UPID = "";
   public string AccessKey = "";
   public string SecretKey = "";
   public string MultiplayFleetID = "";
   public string DefaultRegion = "";


   public string URLAndUPID()
   {
      return MatchmakingURL + "/" + UPID;
   }
}
