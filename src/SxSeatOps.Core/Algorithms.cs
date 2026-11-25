namespace SxSeatOps.Core;

/// <summary>Deterministic search and sorting algorithms used by seat operations.</summary>
public static class Algorithms
{
    /// <summary>Binary-searches a sorted list; a missing value returns the bitwise complement of insertion index.</summary>
    public static int BinarySearch<T>(IReadOnlyList<T> list,T value,IComparer<T> comparer)
    {ArgumentNullException.ThrowIfNull(list);ArgumentNullException.ThrowIfNull(comparer);int lo=0,hi=list.Count-1;while(lo<=hi){int mid=lo+((hi-lo)>>1);int c=comparer.Compare(list[mid],value);if(c==0)return mid;if(c<0)lo=mid+1;else hi=mid-1;}return ~lo;}
    /// <summary>Returns the first index whose value is not less than target.</summary>
    public static int LowerBound<T>(IReadOnlyList<T> list,T target,IComparer<T> comparer)
    {int lo=0,hi=list.Count;while(lo<hi){int mid=lo+((hi-lo)>>1);if(comparer.Compare(list[mid],target)<0)lo=mid+1;else hi=mid;}return lo;}
    /// <summary>Returns the first index whose value is greater than target.</summary>
    public static int UpperBound<T>(IReadOnlyList<T> list,T target,IComparer<T> comparer)
    {int lo=0,hi=list.Count;while(lo<hi){int mid=lo+((hi-lo)>>1);if(comparer.Compare(list[mid],target)<=0)lo=mid+1;else hi=mid;}return lo;}
    /// <summary>Stable merge sort with insertion sort for small ranges.</summary>
    public static void StableSort<T>(IList<T> list,IComparer<T> comparer,int insertionThreshold=16)
    {
        ArgumentNullException.ThrowIfNull(list);ArgumentNullException.ThrowIfNull(comparer);if(insertionThreshold<1)throw new ArgumentOutOfRangeException(nameof(insertionThreshold));int n=list.Count;if(n<2)return;
        T[] a=new T[n],tmp=new T[n];for(int i=0;i<n;i++)a[i]=list[i];Sort(0,n);
        for(int i=0;i<n;i++)list[i]=a[i];
        void Sort(int start,int length)
        {
            if(length<=insertionThreshold){for(int i=start+1;i<start+length;i++){T item=a[i];int j=i-1;while(j>=start&&comparer.Compare(a[j],item)>0){a[j+1]=a[j];j--;}a[j+1]=item;}return;}
            int left=length/2;Sort(start,left);Sort(start+left,length-left);int m=start+left,end=start+length,i1=start,i2=m,outIndex=start;
            while(i1<m&&i2<end)tmp[outIndex++]=comparer.Compare(a[i1],a[i2])<=0?a[i1++]:a[i2++];while(i1<m)tmp[outIndex++]=a[i1++];while(i2<end)tmp[outIndex++]=a[i2++];for(int k=start;k<end;k++)a[k]=tmp[k];
        }
    }
    /// <summary>Determines whether a list is ordered according to a comparer.</summary>
    public static bool IsSorted<T>(IReadOnlyList<T> list,IComparer<T> comparer){for(int i=1;i<list.Count;i++)if(comparer.Compare(list[i-1],list[i])>0)return false;return true;}
}
/// <summary>Passenger upgrade priority: tier, miles, check-in instant, then PNR.</summary>
public sealed class UpgradePriorityComparer:IComparer<Passenger>
{
    /// <inheritdoc/>
    public int Compare(Passenger? x,Passenger? y)
    {if(ReferenceEquals(x,y))return 0;if(x is null)return 1;if(y is null)return -1;int r=y.Tier.CompareTo(x.Tier);if(r!=0)return r;r=y.Miles.CompareTo(x.Miles);if(r!=0)return r;r=x.CheckIn.CompareTo(y.CheckIn);return r!=0?r:StringComparer.Ordinal.Compare(x.Pnr,y.Pnr);}
}
/// <summary>Seat-map searching extensions.</summary>
public static class SeatSearch
{
    /// <summary>Finds the first available seat matching required and forbidden features.</summary>
    public static SeatId? FindFirstFree(SeatMap map,CabinClass cabin,SeatAttributes required,SeatAttributes forbidden)
    {Seat? seat=map.FindFirstFree(cabin,required,forbidden);return seat?.Id;}
    /// <summary>Finds the first adjacent group in one row and one aisle-separated block.</summary>
    public static bool TryFindAdjacentGroup(SeatMap map,CabinClass cabin,int size,out IReadOnlyList<SeatId> seats)
    {
        ArgumentNullException.ThrowIfNull(map);seats=Array.Empty<SeatId>();if(size<1)return false;
        IReadOnlyList<Seat> candidates=map.GetSeats(cabin);for(int start=0;start<candidates.Count;start++)
        {
            Seat first=candidates[start];if(first.Status!=SeatStatus.Available)continue;var group=new List<SeatId>(size){first.Id};Seat previous=first;
            for(int j=start+1;j<candidates.Count&&group.Count<size;j++)
            {Seat next=candidates[j];if(next.Id.Row!=first.Id.Row)break;if(next.Status!=SeatStatus.Available||IsGangBetween(map.Layout,previous.Id,next.Id))break;group.Add(next.Id);previous=next;}
            if(group.Count==size){seats=group.AsReadOnly();return true;}
        }
        return false;
    }
    private static bool IsGangBetween(CabinLayout layout,SeatId left,SeatId right)
    {if(!CabinLayout.TryGetLayout(left.Row,out _,out string? pattern)||pattern is null)return true;int a=pattern.IndexOf(left.Column),b=pattern.IndexOf(right.Column);if(a<0||b<0)return true;for(int i=a+1;i<b;i++)if(pattern[i]=='-')return true;return false;}
}
