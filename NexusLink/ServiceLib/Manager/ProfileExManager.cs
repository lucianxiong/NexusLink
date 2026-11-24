namespace ServiceLib.Manager;

public class ProfileExManager
{
    private static readonly Lazy<ProfileExManager> _instance = new(() => new());
    private ConcurrentBag<ProfileExItem> _lstProfileEx = [];
    private readonly Queue<string> _queIndexIds = new();
    public static ProfileExManager Instance => _instance.Value;
    private static readonly string _tag = "ProfileExHandler";

    public ProfileExManager()
    {
        //Init();
    }

    public async Task Init()
    {
        await InitData();
    }


    private async Task InitData()
    {
        await SQLiteHelper.Instance.ExecuteAsync($"delete from ProfileExItem where indexId not in ( select indexId from ProfileItem )");

        _lstProfileEx = new(await SQLiteHelper.Instance.TableAsync<ProfileExItem>().ToListAsync());
    }

    private void IndexIdEnqueue(string indexId)
    {
        if (indexId.IsNotEmpty() && !_queIndexIds.Contains(indexId))
        {
            _queIndexIds.Enqueue(indexId);
        }
    }

    private async Task SaveQueueIndexIds()
    {
        var cnt = _queIndexIds.Count;
        if (cnt > 0)
        {
            var lstExists = await SQLiteHelper.Instance.TableAsync<ProfileExItem>().ToListAsync();
            List<ProfileExItem> lstInserts = [];
            List<ProfileExItem> lstUpdates = [];

            for (var i = 0; i < cnt; i++)
            {
                var id = _queIndexIds.Dequeue();
                var item = lstExists.FirstOrDefault(t => t.IndexId == id);
                var itemNew = _lstProfileEx?.FirstOrDefault(t => t.IndexId == id);
                if (itemNew is null)
                {
                    continue;
                }

                if (item is not null)
                {
                    lstUpdates.Add(itemNew);
                }
                else
                {
                    lstInserts.Add(itemNew);
                }
            }

            try
            {
                if (lstInserts.Count > 0)
                {
                    await SQLiteHelper.Instance.InsertAllAsync(lstInserts);
                }

                if (lstUpdates.Count > 0)
                {
                    await SQLiteHelper.Instance.UpdateAllAsync(lstUpdates);
                }
            }
            catch (Exception ex)
            {
                Logging.SaveLog(_tag, ex);
            }
        }
    }

    private ProfileExItem AddProfileEx(string indexId)
    {
        var profileEx = new ProfileExItem()
        {
            IndexId = indexId,
            Delay = 0,
            Speed = 0,
            Sort = 0,
            Message = string.Empty
        };
        _lstProfileEx.Add(profileEx);
        IndexIdEnqueue(indexId);
        return profileEx;
    }

    private ProfileExItem GetProfileExItem(string? indexId)
    {
        return _lstProfileEx.FirstOrDefault(t => t.IndexId == indexId) ?? AddProfileEx(indexId);
    }


    public async Task SaveTo()
    {
        try
        {
            await SaveQueueIndexIds();
        }
        catch (Exception ex)
        {
            Logging.SaveLog(_tag, ex);
        }
    }

    public void SetTestDelay(string indexId, int delay)
    {
        var profileEx = GetProfileExItem(indexId);

        profileEx.Delay = delay;
        IndexIdEnqueue(indexId);
    }



    public void SetTestIpInfo(string indexId, string ipInfo)
    {
        var profileEx = GetProfileExItem(indexId);

        profileEx.IpInfo = ipInfo;
        IndexIdEnqueue(indexId);
    }

    public void SetSort(string indexId, int sort)
    {
        var profileEx = GetProfileExItem(indexId);

        profileEx.Sort = sort;
        IndexIdEnqueue(indexId);
    }


    public int GetMaxSort()
    {
        if (_lstProfileEx.Count <= 0)
        {
            return 0;
        }
        return _lstProfileEx.Max(t => t?.Sort ?? 0);
    }
}
