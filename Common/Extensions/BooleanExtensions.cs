namespace SNIBypassGUI.Common
{
    public static class BooleanExtensions
    {
        /// <summary>
        /// Converts a boolean value to "是" (Yes) or "否" (No).
        /// </summary>
        /// <param name="value">The boolean value.</param>
        /// <returns>"是" if true; otherwise "否".</returns>
        public static string ToYesNo(this bool value) => value ? "是" : "否";

        /// <summary>
        /// Converts a boolean value to "开" (On) or "关" (Off).
        /// </summary>
        /// <param name="value">The boolean value.</param>
        /// <returns>"开" if true; otherwise "关".</returns>
        public static string ToOnOff(this bool value) => value ? "开" : "关";
    }
}
