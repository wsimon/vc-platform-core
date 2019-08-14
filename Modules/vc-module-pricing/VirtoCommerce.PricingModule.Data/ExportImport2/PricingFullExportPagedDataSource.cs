using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using VirtoCommerce.ExportModule.Core.Model;

namespace VirtoCommerce.PricingModule.Data.ExportImport
{
    public class PricingFullExportPagedDataSource : IPagedDataSource
    {
        private class ExportDataSourceState
        {
            public ExportDataSourceState()
            {
                Result = Array.Empty<IExportable>();
            }

            public int TotalCount;
            public int ReceivedCount;
            public ExportDataQuery DataQuery;
            public IEnumerable<IExportable> Result;
            public Func<ExportDataQuery, IPagedDataSource> DataSourceFactory;
            public int GetNextTake(int pageSize)
            {
                return pageSize - TotalCount - ReceivedCount < 0 ? pageSize : pageSize - TotalCount - ReceivedCount;
            }
        }

        public int TotalCount { get; set; }
        public int CurrentPageNumber { get; protected set; }
        public int PageSize { get; set; } = 50;

        private readonly Func<ExportDataQuery, PricelistExportPagedDataSource> _pricelistDataSourceFactory;
        private readonly Func<ExportDataQuery, PricelistAssignmentExportPagedDataSource> _assignmentsDataSourceFactory;
        private readonly Func<ExportDataQuery, PriceExportPagedDataSource> _pricesDataSourceFactory;

        private List<ExportDataSourceState> _exportDataSourceStates;
        public ExportDataQuery DataQuery
        {
            set
            {
                _dataQuery = value;
                CurrentPageNumber = 0;
                TotalCount = -1;

                _exportDataSourceStates = new List<ExportDataSourceState>()
                {
                    new ExportDataSourceState() {DataQuery = BuildExportDataQuery<PricelistExportDataQuery>(), DataSourceFactory = query =>  _pricelistDataSourceFactory(query)  },
                    new ExportDataSourceState() {DataQuery = BuildExportDataQuery<PricelistAssignmentExportDataQuery>(), DataSourceFactory =  query => _assignmentsDataSourceFactory(query)},
                    new ExportDataSourceState() {DataQuery = BuildExportDataQuery<PriceExportDataQuery>(), DataSourceFactory =  query =>_pricesDataSourceFactory(query)},
                };
                CalculateCounts();
            }
        }

        private ExportDataQuery _dataQuery;

        public PricingFullExportPagedDataSource(Func<ExportDataQuery, PricelistExportPagedDataSource> pricelistDataSourceFactory,
            Func<ExportDataQuery, PricelistAssignmentExportPagedDataSource> assignmentsDataSourceFactory,
            Func<ExportDataQuery, PriceExportPagedDataSource> pricesDataSourceFactory)
        {
            _pricelistDataSourceFactory = pricelistDataSourceFactory;
            _assignmentsDataSourceFactory = assignmentsDataSourceFactory;
            _pricesDataSourceFactory = pricesDataSourceFactory;
        }

        public int GetTotalCount()
        {
            CalculateCounts();
            return TotalCount;
        }

        public virtual IEnumerable<IExportable> FetchNextPage()
        {
            int takeNext = PageSize;
            var taskList = new List<Task>();

            foreach (var state in _exportDataSourceStates)
            {
                state.Result = Array.Empty<IExportable>();
                if (state.ReceivedCount < state.TotalCount)
                {
                    state.DataQuery.Take = takeNext;
                    state.DataQuery.Skip = CurrentPageNumber * PageSize;
                    taskList.Add(Task.Factory.StartNew(() => { state.Result = state.DataSourceFactory(state.DataQuery).FetchNextPage().ToArray(); }));
                    takeNext = state.GetNextTake(PageSize);
                }
            }

            Task.WhenAll(taskList).GetAwaiter().GetResult();
            var result = new List<IExportable>();

            foreach (var state in _exportDataSourceStates)
            {
                result.AddRange(state.Result);
                state.ReceivedCount = state.Result.Count();
            }
            CurrentPageNumber++;

            return result;
        }


        private void CalculateCounts()
        {
            var taskList = new List<Task>();
            foreach (var state in _exportDataSourceStates)
            {
                state.DataQuery.Skip = 0;
                state.DataQuery.Take = 0;

                taskList.Add(Task.Factory.StartNew(() => { state.TotalCount = state.DataSourceFactory(state.DataQuery).GetTotalCount(); }));
            }

            Task.WhenAll(taskList).GetAwaiter().GetResult();

            TotalCount = _exportDataSourceStates.Sum(x => x.TotalCount);

        }
        private T BuildExportDataQuery<T>() where T : ExportDataQuery, new()
        {
            var newExportDataQuery = new T();
            newExportDataQuery.Skip = _dataQuery.Skip;
            newExportDataQuery.Take = _dataQuery.Take;
            newExportDataQuery.ObjectIds = _dataQuery.ObjectIds;
            newExportDataQuery.Sort = _dataQuery.Sort;
            return newExportDataQuery;
        }
    }
}
