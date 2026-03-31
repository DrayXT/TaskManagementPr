namespace TaskManagementPr.Data
{
    public static class Constants
    {
        public const string DatabaseFilename = "AppSQLite.db3";

        public static string DatabasePath =>
            $"Data Source={Path.Combine(FileSystem.AppDataDirectory, DatabaseFilename)}";
        public const string SupabaseUrl = "https://vggvvmhlqrksglxsgvmz.supabase.co";
        public const string SupabaseAnonKey = "sb_publishable_HDJh79fPkY2LyCm2_S6fpA_dbzZ_BgH";
    }
}