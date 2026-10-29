namespace BCC.Core;

public static class SafetyDisclosure
{
    public const string CrossBoot =
        "IMPORTANT — BEFORE CHANGING A CHARGING LIMIT\r\n\r\n" +
        "Your selected limit is saved for this Windows installation across sign-ins, restarts and shutdowns. " +
        "Where supported, the app's optional agent attempts to restore the original firmware limit at normal Windows shutdown, " +
        "then reapplies your saved limit at the next boot of the same Windows installation.\r\n\r\n" +
        "THIS CANNOT BE GUARANTEED when booting Linux, another Windows installation or an external drive. " +
        "A firmware setting may remain in effect after a crash, power loss, hibernation/Fast Startup, a failed shutdown service, " +
        "or a change in the laptop's firmware or drivers. Windows cannot run the restoration agent after another OS takes over.\r\n\r\n" +
        "The application will not assume your original setting was 100%. If it cannot read and safely restore the original setting, " +
        "it refuses the hardware change. Even a matching firmware readback does NOT prove the battery physically stops charging at that percentage.\r\n\r\n" +
        "Before booting another system, use 'Prepare for another OS' when possible. " +
        "You can restore the original charging setting manually. No installer can guarantee restoration after unexpected power loss.";

}
