public readonly struct LanPlayerProfileMessage
{
    public readonly LanRoomRole role;
    public readonly UserProfileData profile;

    public LanPlayerProfileMessage(LanRoomRole role, UserProfileData profile)
    {
        this.role = role;
        this.profile = profile;
    }
}
