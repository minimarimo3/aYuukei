using UnityEngine;

namespace TutorialInfo
{
    [CreateAssetMenu(fileName = "Readme", menuName = "Tutorial/Readme")]
    public sealed class Readme : ScriptableObject
    {
        [TextArea]
        public string body = string.Empty;
    }
}
